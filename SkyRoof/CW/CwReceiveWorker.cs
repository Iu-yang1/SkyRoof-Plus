namespace SkyRoof.CW
{
  /// <summary>
  /// Receive-side scheduler that keeps carrier tracking independent from
  /// neural inference. The tracker runs at a fixed cadence; DeepCW consumes
  /// immutable PCM snapshots on a separate latest-only lane. If inference is
  /// still busy when the next hop becomes due, that old window is skipped
  /// rather than queued.
  /// </summary>
  public sealed partial class CwReceiveWorker : IDisposable
  {
    public CwTranscriptArchive History { get; }
    private readonly object sync = new();
    private readonly CwPcmIngress ingress;
    private readonly CwReceiveWorkerOptions options;
    private readonly Func<CwDecoderMode,ICwInferenceSession?> sessionFactory;
    private readonly Func<double, IReadOnlyList<CwSignalTrack>>
      trackAnalyzer;

    private CancellationTokenSource? stop;
    private Task? trackingTask;
    private Task? inferenceTask;
    private ICwInferenceSession? session;
    private CwDecoderMode decoderMode;
    private CwDecoderMode? sessionMode;
    private long modeRevision;
    private long appliedTrackingModeRevision=-1;

    private IReadOnlyList<CwSignalTrack> latestTracks =
      Array.Empty<CwSignalTrack>();
    private CwContinuousDecodeBatch? latestDecode;

    private long observedGeneration = -1;
    private long lastInferenceAttemptSampleIndex = -1;
    private long trackingCycles;
    private long completedInferenceWindows;
    private long skippedInferenceWindows;

    private int inferenceBusy;
    private int pendingSessionReset;
    private bool disposed;
    private CwReceiveModelState modelState =
      CwReceiveModelState.Unknown;
    private DateTime nextModelAttemptUtc =
      DateTime.MinValue;
    private string? lastError;
    private double knownDopplerRateHzPerSecond;

    public event EventHandler<CwTracksUpdatedEventArgs>?
      TracksUpdated;
    public event EventHandler<CwDecodeUpdatedEventArgs>?
      DecodeUpdated;
    public event EventHandler? StatusChanged;

    public CwReceiveWorker(
      CwPcmIngress ingress,
      CwReceiveWorkerOptions? options = null)
      : this(
          ingress,
          options,
          null,
          null)
    {
    }

    internal CwReceiveWorker(
      CwPcmIngress ingress,
      CwReceiveWorkerOptions? options,
      Func<ICwInferenceSession?>? sessionFactory,
      Func<double, IReadOnlyList<CwSignalTrack>>?
        trackAnalyzer)
    {
      ArgumentNullException.ThrowIfNull(ingress);
      this.ingress = ingress;
      this.options =
        options ??
        new CwReceiveWorkerOptions();
      decoderMode=this.options.DecoderMode;

      ValidateOptions(
        this.options);

      if (this.options.DecodeWindowSeconds >
          ingress.FrontEnd.Audio.CapacitySeconds)
        throw new ArgumentException(
          "CW audio history is shorter than the decode window.",
          nameof(options));

      History = new CwTranscriptArchive(this.options.HistoryDirectory);
      this.sessionFactory =
        sessionFactory!=null
          ? _=>sessionFactory()
          : mode=>mode==CwDecoderMode.NewCW
            ? new NewCwInferenceSession(this.options)
            : DeepCwModelManager.IsInstalled()
              ? new DeepCwInferenceSession(this.options)
              : null;

      this.trackAnalyzer =
        trackAnalyzer ??
        (dopplerRate =>
          this.ingress.FrontEnd.Analyze(
            dopplerRate));
    }

    public CwReceiveWorkerOptions Options =>
      options;

    public CwDecoderMode DecoderMode
    {
      get { lock(sync)return decoderMode; }
    }

    // Mode switching keeps the audio input and CAT/TX sessions alive.
    // The inference task alone owns session.Dispose(), so this cannot
    // dispose native ONNX while it is decoding on another thread.
    public void SetDecoderMode(CwDecoderMode next)
    {
      if(!Enum.IsDefined(next))
        throw new ArgumentOutOfRangeException(nameof(next));
      ThrowIfDisposed();
      lock(sync)
      {
        if(decoderMode==next)return;
        decoderMode=next;
        modeRevision++;
        lastInferenceAttemptSampleIndex=-1;
        latestDecode=null;
        modelState=CwReceiveModelState.Unknown;
        nextModelAttemptUtc=DateTime.MinValue;
        lastError=null;
      }
      OnStatusChanged();
    }

    public double KnownDopplerRateHzPerSecond
    {
      get => Volatile.Read(
        ref knownDopplerRateHzPerSecond);
      set
      {
        if (!double.IsFinite(value))
          throw new ArgumentOutOfRangeException(
            nameof(value));
        Volatile.Write(
          ref knownDopplerRateHzPerSecond,
          value);
      }
    }

    public IReadOnlyList<CwSignalTrack> LatestTracks
    {
      get
      {
        lock (sync)
          return latestTracks.ToArray();
      }
    }

    public CwContinuousDecodeBatch? LatestDecode
    {
      get
      {
        lock (sync)
          return latestDecode;
      }
    }

    public void Start()
    {
      ThrowIfDisposed();

      lock (sync)
      {
        if (trackingTask != null)
          return;

        stop =
          new CancellationTokenSource();
        trackingTask =
          Task.Run(
            () => TrackingLoopAsync(
              stop.Token));
      }

      OnStatusChanged();
    }

    public async Task StopAsync()
    {
      Task? tracker;
      Task? inference;
      CancellationTokenSource? tokenSource;

      lock (sync)
      {
        tracker = trackingTask;
        inference = inferenceTask;
        tokenSource = stop;
        trackingTask = null;
        stop = null;
      }

      if (tracker == null &&
          inference == null)
        return;

      try
      {
        tokenSource?.Cancel();
      }
      catch (ObjectDisposedException)
      {
        // A racing Stop/Dispose may invalidate an already cancelled source.
        Serilog.Log.Debug("CW receive stop token was already disposed");
      }

      if (tracker != null)
      {
        try
        {
          await tracker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when(tokenSource?.IsCancellationRequested == true)
        {
          Serilog.Log.Debug("CW tracker cancelled during receiver shutdown");
        }
      }

      // The tracker can schedule its final inference after Stop captured the
      // previous task. Re-read only after tracking has stopped.
      lock (sync) inference = inferenceTask;
      if (inference != null)
      {
        try
        {
          await inference.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when(tokenSource?.IsCancellationRequested == true)
        {
          Serilog.Log.Debug("CW inference cancelled during receiver shutdown");
        }
      }

      tokenSource?.Dispose();

      lock (sync)
      {
        inferenceTask = null;
        session?.Dispose();
        session = null;
        sessionMode=null;
        latestTracks =
          Array.Empty<CwSignalTrack>();
        latestDecode = null;
      }

      Interlocked.Exchange(
        ref inferenceBusy,
        0);

      OnStatusChanged();
    }
  }
}
