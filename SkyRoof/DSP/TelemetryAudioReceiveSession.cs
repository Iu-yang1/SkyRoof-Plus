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
  /// Converts 48-kHz REAL AF or a REAL 12-kHz IF into positive-frequency
  /// analytic IQ, then frequency-translates its selected carrier to zero.
  /// A centered Hilbert FIR and a continuous NCO preserve phase across
  /// arbitrary WASAPI block boundaries. Raw IF is not FM/SSB-demodulated:
  /// the existing SkySignals IQ modem performs the final modem demodulation.
  /// </summary>
  public sealed class TelemetryRealAudioIqConverter
  {
    private const int Rate = 48000;
    private const int Taps = 129;
    private const int Delay = (Taps - 1) / 2;
    private readonly float[] history = new float[Taps];
    private readonly double[] hilbert = new double[Taps];
    private readonly double omega;
    private int next;
    private double phase;

    public int CenterFrequencyHz { get; }

    public TelemetryRealAudioIqConverter(int centerFrequencyHz)
    {
      if (centerFrequencyHz <= 0 || centerFrequencyHz >= Rate / 2)
        throw new ArgumentOutOfRangeException(nameof(centerFrequencyHz));
      CenterFrequencyHz = centerFrequencyHz;
      omega = 2.0 * Math.PI * centerFrequencyHz / Rate;
      foreach (int i in Enumerable.Range(0, Taps))
      {
        int lag = i - Delay;
        if (lag == 0 || (lag & 1) == 0) continue;
        double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (Taps - 1));
        // Positive-frequency analytic representation: cos(wt) + j sin(wt).
        hilbert[i] = 2.0 * window / (Math.PI * lag);
      }
    }

    public Complex32[] Convert(ReadOnlySpan<float> input)
    {
      var result = new Complex32[input.Length];
      int i = 0;
      foreach (float sample in input)
      {
        history[next] = float.IsFinite(sample) ? sample : 0;
        int delayed = next - Delay;
        if (delayed < 0) delayed += Taps;
        double real = history[delayed];
        double imaginary = 0;
        int read = next;
        for (int k = 0; k < Taps; k++)
        {
          imaginary += hilbert[k] * history[read];
          if (--read < 0) read = Taps - 1;
        }
        if (++next == Taps) next = 0;
        double c = Math.Cos(phase), sin = Math.Sin(phase);
        result[i++] = new Complex32(
          (float)(real * c + imaginary * sin),
          (float)(imaginary * c - real * sin));
        // The mixer below multiplies IQ by exp(-j*phase), so the
        // NCO phase must INCREASE to translate positive carrier to DC.
        phase += omega;
        if (phase > Math.PI) phase -= 2 * Math.PI;
      }
      return result;
    }

    public void Reset()
    {
      Array.Clear(history);
      next = 0;
      phase = 0;
    }
  }

  /// <summary>
  /// Compatibility bridge for already FM-discriminated AFSK audio.
  /// The vendored SkyTlm AFSK modem expects IQ of an FM carrier and applies
  /// its own FM discriminator. Frequency-modulating an ideal analytic carrier
  /// with the existing AF waveform makes the discriminator recover that AF
  /// once, instead of erroneously differentiating its Hilbert representation.
  /// </summary>
  public sealed class TelemetryAfskFmIqConverter
  {
    private const double Rate = 48000.0;
    private const double MaxDeviationHz = 1800.0;
    private double phase;

    public Complex32[] Convert(ReadOnlySpan<float> audio)
    {
      var result = new Complex32[audio.Length];
      for (int i = 0; i < audio.Length; i++)
      {
        double sample = float.IsFinite(audio[i]) ? Math.Clamp(audio[i], -1f, 1f) : 0;
        phase += 2 * Math.PI * MaxDeviationHz * sample / Rate;
        if (phase > Math.PI) phase -= 2 * Math.PI;
        if (phase < -Math.PI) phase += 2 * Math.PI;
        result[i] = new Complex32((float)Math.Cos(phase), (float)Math.Sin(phase));
      }
      return result;
    }

    public void Reset() => phase = 0;
  }

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
    private bool disposed;
    private readonly record struct AudioBlock(float[] Samples, DateTime Utc, bool Discontinuity);
    internal event EventHandler<DataEventArgs<Complex32>>? IqAvailable;
    internal event Action? IqDiscontinuity;
    internal string Status { get; private set; } = "SDR IQ";
    internal long DroppedBlocks => Interlocked.Read(ref droppedBlocks);
    internal long ReceivedSamples => Interlocked.Read(ref receivedSamples);
    private long discontinuities;
    private int afModulation = (int)Modulation.Unknown;
    private bool isAfInput;
    internal long Discontinuities => Interlocked.Read(ref discontinuities);
    internal void SetAfModulation(Modulation modulation) =>
      Volatile.Write(ref afModulation, (int)modulation);

    internal TelemetryAudioReceiveSession()
    {
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
      Interlocked.Exchange(ref discontinuities, 0);
      worker = Task.Run(() => RunAsync(next.Reader, cancel.Token, version, centerHz));
      try
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
              lanFlush = Task.Run(() => FlushLanAsync(cancel.Token));
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
            sharedIf = ifAf!;
            sharedIsAf = true;
            ifAf!.DemodulatedAfAvailable += OnSamples;
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
      var samples = new float[e.Count];
      Array.Copy(e.Data, 0, samples, 0, e.Count);
      QueueAudio(samples, e.Utc, false);
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

    private void QueueAudio(float[] input, DateTime utc, bool discontinuity)
    {
      var writer = queue?.Writer;
      if (writer == null || input.Length == 0) return;
      var samples = (float[])input.Clone();
      Interlocked.Add(ref receivedSamples, samples.Length);
      if (!writer.TryWrite(new AudioBlock(samples, utc, discontinuity)))
        Interlocked.Increment(ref droppedBlocks);
    }

    private async Task RunAsync(ChannelReader<AudioBlock> reader,
      CancellationToken cancel, int version, int centerHz)
    {
      var converter = new TelemetryRealAudioIqConverter(centerHz);
      var fmBridge = new TelemetryAfskFmIqConverter();
      int appliedModulation = Volatile.Read(ref afModulation);
      long observedDrops = 0;
      try
      {
        await foreach (AudioBlock frame in reader.ReadAllAsync(cancel))
        {
          if (version != Volatile.Read(ref epoch)) return;
          long drops = Interlocked.Read(ref droppedBlocks);
          int wantedModulation = Volatile.Read(ref afModulation);
          if (drops != observedDrops || frame.Discontinuity ||
              wantedModulation != appliedModulation)
          {
            converter.Reset();
            fmBridge.Reset();
            observedDrops = drops;
            appliedModulation = wantedModulation;
            Interlocked.Increment(ref discontinuities);
            IqDiscontinuity?.Invoke();
          }
          Complex32[] iq = isAfInput && appliedModulation == (int)Modulation.AFSK
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
        if (sharedIsAf) ifAf.DemodulatedAfAvailable -= OnSamples;
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
