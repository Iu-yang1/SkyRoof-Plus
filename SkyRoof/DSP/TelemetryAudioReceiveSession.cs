using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MathNet.Numerics;
using NAudio.CoreAudioApi;
using SkyRoof.IF;
using Serilog;
using VE3NEA;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof
{
  /// <summary>
  /// One selected telemetry audio input. Soundcard callbacks and the shared
  /// IF/AF stream only COPY PCM into a bounded queue. Hilbert and the
  /// existing IQ telemetry decoder run outside the capture callback.
  /// </summary>
  internal sealed class TelemetryAudioReceiveSession : IDisposable
  {
    private InputSoundcard<float>? capture;
    private readonly LoopbackInputSoundcard loopback = new();
    private IfAfService? sharedIf;
    private bool sharedIsAf;
    private bool sharedIsRaw;
    private IcomDirectLanMediaOwner? lanOwner;
    private readonly IcomNetworkIfPcmPipeline lanPcm = new();
    private readonly object lanSync = new();
    private Task? lanFlush;
    private Channel<AudioBlock>? queue;
    private CancellationTokenSource? stop;
    private Task? worker;
    private int epoch;
    private long droppedBlocks;
    private long receivedSamples;
    private long inactiveBlocks;
    private readonly Func<bool> needsLiveIq;
    private bool disposed;
    private readonly record struct AudioBlock(float[] Samples, DateTime Utc, bool Discontinuity);
    internal event EventHandler<DataEventArgs<Complex32>>? IqAvailable;
    internal event Action? IqDiscontinuity;
    internal string Status { get; private set; } = "SDR IQ";
    internal long DroppedBlocks => Interlocked.Read(ref droppedBlocks);
    internal long ReceivedSamples => Interlocked.Read(ref receivedSamples);
    private long discontinuities;
    private double inputRmsDbfs = double.NegativeInfinity;
    internal double InputRmsDbfs => Volatile.Read(ref inputRmsDbfs);
    private int afModulation = (int)Modulation.Unknown;
    private SignalParams? afSignal;
    private bool isAfInput;
    internal long Discontinuities => Interlocked.Read(ref discontinuities);
    internal void SetAfModulation(Modulation modulation) =>
      Volatile.Write(ref afModulation, (int)modulation);

    internal void SetAfSignalParams(SignalParams? signal)
    {
      Volatile.Write(ref afSignal, signal);
      SetAfModulation(signal?.Modulation ?? Modulation.Unknown);
    }

    internal static bool UseDiscriminatorAf(bool isAf, Modulation modulation,
      TelemetryFskAfInput fskAfInput) =>
      isAf && (modulation == Modulation.AFSK ||
        (fskAfInput == TelemetryFskAfInput.FmDiscriminator &&
         modulation is Modulation.FSK or Modulation.GFSK or Modulation.GMSK));

    internal static double FskAfPeakDeviation(SignalParams? signal, double overrideHz)
    {
      if (overrideHz > 0) return overrideHz;
      return signal?.Deviation is double d && d > 0 ? d :
        signal is { Baud: > 0 } p ? p.Baud / 4.0 : 1800;
    }

    internal TelemetryAudioReceiveSession(Func<bool>? needsLiveIq = null)
    {
      // An open telemetry pane is not necessarily decoding: without a live
      // satellite decoder, do not spend CPU on Hilbert/NCO and IQ allocations.
      this.needsLiveIq = needsLiveIq ?? (() => true);
      loopback.SamplesAvailable += OnSamples;
    }

    internal void Start(TelemetrySettings config, IfAfService? ifAf, Context? ctx = null)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      Stop();
      if (config.InputSource == TelemetryReceiveSource.SdrIq)
      {
        Status = "SDR IQ (48 kHz)";
        return;
      }

      bool shared = config.InputSource is TelemetryReceiveSource.Shared12kIf or
        TelemetryReceiveSource.IfAfDemodulatedAf;
      if (config.InputSource == TelemetryReceiveSource.IfAfDemodulatedAf &&
          ifAf?.IsRunning != true)
        throw new InvalidOperationException(
          "Start IF->AF first to share its demodulated AF.");
      string? device = config.InputSource switch
      {
        TelemetryReceiveSource.Capture12kIf => config.IfCaptureDeviceId,
        TelemetryReceiveSource.CaptureAf => config.AfCaptureDeviceId,
        TelemetryReceiveSource.LoopbackAf => config.AfLoopbackDeviceId,
        _ => null
      };
      if (!shared && string.IsNullOrWhiteSpace(device))
        throw new InvalidOperationException("Select the telemetry audio endpoint in Settings > Telemetry.");
      if (!Enum.IsDefined(config.InputSource))
        throw new ArgumentOutOfRangeException(nameof(config.InputSource));
      bool realIf = config.InputSource is TelemetryReceiveSource.Shared12kIf or
        TelemetryReceiveSource.Capture12kIf;
      isAfInput = !realIf;
      int centerHz = realIf ? 12000 : config.AfCenterHz;
      if (centerHz <= 0 || centerHz >= 24000)
        throw new ArgumentOutOfRangeException(nameof(config.AfCenterHz));

      var next = Channel.CreateBounded<AudioBlock>(new BoundedChannelOptions(16)
      {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
      });
      var cancel = new CancellationTokenSource();
      int version = ++epoch;
      queue = next;
      stop = cancel;
      Interlocked.Exchange(ref droppedBlocks, 0);
      Interlocked.Exchange(ref receivedSamples, 0);
      Interlocked.Exchange(ref inactiveBlocks, 0);
      Interlocked.Exchange(ref discontinuities, 0);
      Volatile.Write(ref inputRmsDbfs, double.NegativeInfinity);
      worker = Task.Run(() => RunAsync(next.Reader, cancel.Token, version, centerHz,
        config.FskAfInput, config.FskAfDeviationHz));
      try
      {
        ConnectSource(config, ifAf, ctx, device, cancel.Token);
        Status = config.InputSource == TelemetryReceiveSource.Shared12kIf
          ? $"IF {(lanOwner != null ? "Direct LAN" : sharedIf != null ? "shared USB" : "independent USB")}, {centerHz} Hz center, 48-kHz IQ"
          : $"{config.InputSource}, {centerHz} Hz center, 48-kHz IQ bridge";
      }
      catch
      {
        Stop();
        throw;
      }
    }

    private void ConnectSource(TelemetrySettings config, IfAfService? ifAf,
      Context? ctx, string? device, CancellationToken cancel)
    {
      switch (config.InputSource)
        {
          case TelemetryReceiveSource.Shared12kIf:
            if (ctx?.Settings.IfAf.InputSource == IfAfInputSource.AuthenticatedLanIf)
            {
              lanOwner = ctx.DirectLanMediaOwner ??= new IcomDirectLanMediaOwner(ctx);
              lock (lanSync) lanPcm.Reset();
              lanOwner.RawAudioFrameReceived += OnLanIfFrame;
              lanOwner.GetOrStart();
              lanFlush = Task.Run(() => FlushLanAsync(cancel));
            }
            else if (ifAf?.IsRunning == true)
            {
              sharedIf = ifAf;
              sharedIsRaw = true;
              ifAf.RawIfFrameAvailable += OnRawIfFrame;
            }
            else StartCapture(config.IfCaptureDeviceId, config.IfCaptureChannel);
            break;
          case TelemetryReceiveSource.IfAfDemodulatedAf:
            var afService = ifAf ?? throw new InvalidOperationException(
              "Start IF->AF before sharing demodulated audio.");
            sharedIf = afService;
            sharedIsAf = true;
            afService.CleanDemodulatedAfAvailable += OnSamples;
            break;
          case TelemetryReceiveSource.Capture12kIf:
          case TelemetryReceiveSource.CaptureAf:
            StartCapture(device, config.InputSource == TelemetryReceiveSource.Capture12kIf
              ? config.IfCaptureChannel : AudioChannelChoice.Mix);
            break;
          case TelemetryReceiveSource.LoopbackAf:
            loopback.SetDeviceId(device);
            loopback.Enabled = true;
            if (!loopback.IsRunning)
              throw new InvalidOperationException("Telemetry WASAPI playback loopback did not start.");
            break;
        }
    }

    private void StartCapture(string? device, AudioChannelChoice channel)
    {
      if (string.IsNullOrWhiteSpace(device))
        throw new InvalidOperationException("Select a radio IF/AF capture endpoint.");
      capture = new InputSoundcard<float>(
        captureBufferMilliseconds: 40, readerBlockSizeSamples: 1600,
        channelChoice: channel);
      capture.SamplesAvailable += OnSamples;
      capture.SetDeviceId(device);
      capture.Enabled = true;
      if (!capture.IsRunning)
        throw new InvalidOperationException("Telemetry WASAPI capture did not start.");
    }

    private void OnSamples(object? sender, DataEventArgs<float> e)
    {
      if (e.Count <= 0) return;
      // QueueAudio owns the ONE necessary copy while the capture callback's
      // buffer is valid. The former extra allocation/copy doubled PCM churn.
      QueueAudio(e.Data.AsSpan(0, e.Count), e.Utc, false);
    }

    private void OnRawIfFrame(RawIfPcmFrame frame) =>
      QueueAudio(frame.Samples, frame.Utc, frame.Discontinuity);

    private void OnLanIfFrame(int sampleRate, IcomLanLpcmAudioCodec.RxFrame frame)
    {
      lock (lanSync)
        foreach (var block in lanPcm.Push(sampleRate, frame, DateTime.UtcNow))
          QueueAudio(block.Samples, block.Utc, block.Discontinuity);
    }

    private async Task FlushLanAsync(CancellationToken token)
    {
      using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
      try
      {
        while (await timer.WaitForNextTickAsync(token))
          lock (lanSync)
            foreach (var block in lanPcm.Flush(DateTime.UtcNow))
              QueueAudio(block.Samples, block.Utc, block.Discontinuity);
      }
      catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void QueueAudio(ReadOnlySpan<float> input, DateTime utc, bool discontinuity)
    {
      var writer = queue?.Writer;
      if (writer == null || input.Length == 0) return;
      Interlocked.Add(ref receivedSamples, input.Length);
      double power = 0;
      foreach (float sample in input)
        if (float.IsFinite(sample)) power += (double)sample * sample;
      Volatile.Write(ref inputRmsDbfs,
        10 * Math.Log10(Math.Max(power / input.Length, 1e-15)));
      if (!needsLiveIq())
      {
        // Preserve live level/ingress diagnostics but avoid creating a
        // 48-kHz IQ stream that has no active destination. A fresh decoder
        // starts with clean Hilbert and carrier history after this gap.
        Interlocked.Increment(ref inactiveBlocks);
        return;
      }
      if (!writer.TryWrite(new AudioBlock(input.ToArray(), utc, discontinuity)))
        Interlocked.Increment(ref droppedBlocks);
    }

    private async Task RunAsync(ChannelReader<AudioBlock> reader,
      CancellationToken cancel, int version, int centerHz,
      TelemetryFskAfInput fskAfInput, double fskDeviationHz)
    {
      var converter = new TelemetryRealAudioIqConverter(centerHz);
      var fmBridge = new TelemetryAfskFmIqConverter();
      double bridgeDeviationHz = 1800;
      int appliedModulation = Volatile.Read(ref afModulation);
      long observedDrops = 0;
      long observedInactive = 0;
      try
      {
        await foreach (AudioBlock frame in reader.ReadAllAsync(cancel))
        {
          if (version != Volatile.Read(ref epoch)) return;
          long drops = Interlocked.Read(ref droppedBlocks);
          long inactive = Interlocked.Read(ref inactiveBlocks);
          int wantedModulation = Volatile.Read(ref afModulation);
          if (drops != observedDrops || inactive != observedInactive ||
              frame.Discontinuity || wantedModulation != appliedModulation)
          {
            converter.Reset();
            fmBridge.Reset();
            observedDrops = drops;
            // A previously idle interval has no decoder state to invalidate.
            // On a real input discontinuity or overflow the active decoder
            // must still reset its streaming DSP state.
            bool resetDecoder = drops != observedDrops || frame.Discontinuity ||
              wantedModulation != appliedModulation;
            observedInactive = inactive;
            appliedModulation = wantedModulation;
            if (resetDecoder)
            {
              Interlocked.Increment(ref discontinuities);
              IqDiscontinuity?.Invoke();
            }
          }
          var modulation = (Modulation)appliedModulation;
          bool fmDiscriminated = UseDiscriminatorAf(isAfInput, modulation, fskAfInput);
          if (fmDiscriminated)
          {
            // Bell-202's existing 1800 Hz FM envelope stays unchanged.
            // FSK/GMSK FM-discriminator input uses its own physical deviation.
            double wantedDeviation = modulation == Modulation.AFSK ? 1800 :
              FskAfPeakDeviation(Volatile.Read(ref afSignal), fskDeviationHz);
            if (Math.Abs(wantedDeviation - bridgeDeviationHz) > 0.01)
            {
              fmBridge = new TelemetryAfskFmIqConverter(wantedDeviation);
              bridgeDeviationHz = wantedDeviation;
              Interlocked.Increment(ref discontinuities);
              IqDiscontinuity?.Invoke();
            }
          }
          Complex32[] iq = fmDiscriminated
            ? fmBridge.Convert(frame.Samples) : converter.Convert(frame.Samples);
          if (version != Volatile.Read(ref epoch)) return;
          IqAvailable?.Invoke(this, new DataEventArgs<Complex32>
          {
            Data = iq,
            Count = iq.Length,
            Utc = frame.Utc
          });
        }
      }
      catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
      catch (Exception ex)
      {
        Status = "Telemetry audio input failed: " + ex.Message;
        Log.Error(ex, "Telemetry audio receiver failed");
      }
    }

    internal void Stop()
    {
      Interlocked.Increment(ref epoch);
      if (sharedIf is IfAfService ifAf)
      {
        if (sharedIsAf) ifAf.CleanDemodulatedAfAvailable -= OnSamples;
        if (sharedIsRaw) ifAf.RawIfFrameAvailable -= OnRawIfFrame;
        sharedIf = null;
      }
      sharedIsAf = sharedIsRaw = false;
      if (lanOwner != null)
      {
        lanOwner.RawAudioFrameReceived -= OnLanIfFrame;
        lanOwner = null;
        lock (lanSync) lanPcm.Reset();
      }
      lanFlush = null;
      if (capture != null)
      {
        capture.SamplesAvailable -= OnSamples;
        capture.Enabled = false;
        capture.Dispose();
        capture = null;
      }
      loopback.Enabled = false;
      queue?.Writer.TryComplete();
      queue = null;
      stop?.Cancel();
      stop = null;
      worker = null;
    }

    public void Dispose()
    {
      if (disposed) return;
      Stop();
      disposed = true;
      loopback.SamplesAvailable -= OnSamples;
      loopback.Dispose();
    }
  }
}
