using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MathNet.Numerics;
using NAudio.CoreAudioApi;
using SkyRoof.IF;
using Serilog;
using VE3NEA;

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
  /// One selected telemetry audio input. Soundcard callbacks and the shared
  /// IF/AF stream only COPY PCM into a bounded queue. Hilbert and the
  /// existing IQ telemetry decoder run outside the capture callback.
  /// </summary>
  internal sealed class TelemetryAudioReceiveSession : IDisposable
  {
    private readonly InputSoundcard<float> capture = new(
      captureBufferMilliseconds: 40, readerBlockSizeSamples: 1600);
    private readonly LoopbackInputSoundcard loopback = new();
    private IfAfService? sharedIf;
    private bool sharedIsAf;
    private Channel<AudioBlock>? queue;
    private CancellationTokenSource? stop;
    private Task? worker;
    private int epoch;
    private long droppedBlocks;
    private long receivedSamples;
    private bool disposed;
    private readonly record struct AudioBlock(float[] Samples, DateTime Utc);
    internal event EventHandler<DataEventArgs<Complex32>>? IqAvailable;
    internal string Status { get; private set; } = "SDR IQ";
    internal long DroppedBlocks => Interlocked.Read(ref droppedBlocks);
    internal long ReceivedSamples => Interlocked.Read(ref receivedSamples);

    internal TelemetryAudioReceiveSession()
    {
      capture.SamplesAvailable += OnSamples;
      loopback.SamplesAvailable += OnSamples;
    }

    internal void Start(TelemetrySettings config, IfAfService? ifAf)
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
      if (shared && ifAf?.IsRunning != true)
        throw new InvalidOperationException(
          "Start IF->AF first to share its 48-kHz receiver PCM.");
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
      worker = Task.Run(() => RunAsync(next.Reader, cancel.Token, version, centerHz));
      try
      {
        switch (config.InputSource)
        {
          case TelemetryReceiveSource.Shared12kIf:
          case TelemetryReceiveSource.IfAfDemodulatedAf:
            var sharedHub = ifAf ?? throw new InvalidOperationException(
              "The shared IF-AF receiver is unavailable.");
            sharedIf = sharedHub;
            sharedIsAf = config.InputSource == TelemetryReceiveSource.IfAfDemodulatedAf;
            if (sharedIsAf) sharedHub.DemodulatedAfAvailable += OnSamples;
            else sharedHub.RawIfAvailable += OnSamples;
            break;
          case TelemetryReceiveSource.Capture12kIf:
          case TelemetryReceiveSource.CaptureAf:
            capture.SetDeviceId(device);
            capture.Enabled = true;
            if (!capture.IsRunning)
              throw new InvalidOperationException("Telemetry WASAPI capture did not start.");
            break;
          case TelemetryReceiveSource.LoopbackAf:
            loopback.SetDeviceId(device);
            loopback.Enabled = true;
            if (!loopback.IsRunning)
              throw new InvalidOperationException("Telemetry WASAPI playback loopback did not start.");
            break;
        }
        Status = $"{config.InputSource}, {centerHz} Hz center, 48-kHz IQ bridge";
      }
      catch
      {
        Stop();
        throw;
      }
    }

    private void OnSamples(object? sender, DataEventArgs<float> e)
    {
      var writer = queue?.Writer;
      if (writer == null || e.Count <= 0) return;
      var samples = new float[e.Count];
      Array.Copy(e.Data, 0, samples, 0, e.Count);
      Interlocked.Add(ref receivedSamples, e.Count);
      if (!writer.TryWrite(new AudioBlock(samples, e.Utc)))
        Interlocked.Increment(ref droppedBlocks);
    }

    private async Task RunAsync(ChannelReader<AudioBlock> reader,
      CancellationToken cancel, int version, int centerHz)
    {
      var converter = new TelemetryRealAudioIqConverter(centerHz);
      long observedDrops = 0;
      try
      {
        await foreach (AudioBlock frame in reader.ReadAllAsync(cancel))
        {
          if (version != Volatile.Read(ref epoch)) return;
          long drops = Interlocked.Read(ref droppedBlocks);
          if (drops != observedDrops)
          {
            converter.Reset();
            observedDrops = drops;
          }
          Complex32[] iq = converter.Convert(frame.Samples);
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
        else ifAf.RawIfAvailable -= OnSamples;
        sharedIf = null;
      }
      capture.Enabled = false;
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
      capture.SamplesAvailable -= OnSamples;
      loopback.SamplesAvailable -= OnSamples;
      capture.Dispose();
      loopback.Dispose();
    }
  }
}
