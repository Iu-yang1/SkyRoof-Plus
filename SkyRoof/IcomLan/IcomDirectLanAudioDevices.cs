using SkyRoof.IF;
using VE3NEA;
using Serilog;

namespace SkyRoof
{
  /// <summary>
  /// Windows endpoints for the already-authenticated Direct LAN session.
  /// RX speaker and optional IF→AF are independent consumers of one UDP
  /// receive stream. TX never opens a second radio login or asserts PTT.
  /// </summary>
  internal sealed class IcomDirectLanAudioDevices : IDisposable
  {
    private readonly IcomDirectLanSettings options;
    private readonly IcomLanSpectrumCapture capture;
    private readonly IcomNetworkIfPcmPipeline speakerPcm = new();
    private readonly object speakerSync = new();
    private InputSoundcard<float>? microphone;
    private OutputSoundcard<float>? speakers;
    private CancellationTokenSource? stop;
    private Task? flush;
    private bool disposed;
    internal string Status { get; private set; } = "Audio endpoints not started";

    internal IcomDirectLanAudioDevices(
      IcomDirectLanSettings options, IcomLanSpectrumCapture capture)
    {
      this.options = options;
      this.capture = capture;
    }

    internal void Start()
    {
      if (disposed) throw new ObjectDisposedException(nameof(IcomDirectLanAudioDevices));
      var notes = new List<string>();

      if (options.Receive.Enabled &&
          !string.IsNullOrWhiteSpace(options.Receive.SpeakerDeviceId))
      {
        try
        {
          speakers = new OutputSoundcard<float>(
            options.Receive.SpeakerDeviceId,
            samplingRate: 48000,
            latencyMilliseconds: Math.Clamp(options.Receive.PrebufferMs, 20, 500),
            padSilenceOnUnderrun: true);
          speakers.Volume = Math.Clamp(options.Receive.SpeakerVolume, 0, 100) / 100f;
          speakers.Enabled = true;
          if (!speakers.IsRunning)
            throw new InvalidOperationException("Selected speaker is not running");
          stop = new CancellationTokenSource();
          flush = Task.Run(() => FlushSpeakerAsync(stop.Token));
          notes.Add("RX speaker active");
        }
        catch (Exception ex)
        {
          Log.Warning(ex, "Direct LAN speaker initialization failed");
          speakers?.Dispose();
          speakers = null;
          notes.Add("RX speaker unavailable: " + ex.Message);
        }
      }
      else notes.Add("RX speaker disabled / IF-only");

      if (options.Transmit.Enabled &&
          !string.IsNullOrWhiteSpace(options.Transmit.MicrophoneDeviceId))
      {
        try
        {
          int rate = (int)options.Transmit.SampleRate;
          microphone = new InputSoundcard<float>(
            options.Transmit.MicrophoneDeviceId, rate,
            captureBufferMilliseconds: Math.Clamp(options.Transmit.PrebufferMs, 20, 200),
            readerBlockSizeSamples: Math.Max(128, rate / 50),
            idleSleepMilliseconds: 2);
          microphone.SamplesAvailable += OnMicrophoneSamples;
          microphone.Enabled = true;
          if (!microphone.IsRunning)
            throw new InvalidOperationException("Selected microphone is not running");
          notes.Add("TX microphone active; waiting for CAT PTT");
        }
        catch (Exception ex)
        {
          Log.Warning(ex, "Direct LAN microphone initialization failed");
          if (microphone != null)
          {
            microphone.SamplesAvailable -= OnMicrophoneSamples;
            microphone.Dispose();
            microphone = null;
          }
          notes.Add("TX microphone unavailable: " + ex.Message);
        }
      }
      else notes.Add("TX microphone disabled / no endpoint selected");

      Status = string.Join("; ", notes);
    }

    private void OnMicrophoneSamples(object? sender, DataEventArgs<float> args)
    {
      // The capture owns a bounded queue and checks current CAT PTT both
      // when queueing and again when sending each UDP frame.
      if (!disposed && args.Count > 0)
        capture.TryQueueTransmitAudio(args.Data, args.Count);
    }

    internal void AcceptReceiveAudio(
      int sampleRate, IcomLanLpcmAudioCodec.RxFrame packet)
    {
      if (disposed || speakers == null) return;
      lock (speakerSync)
      {
        foreach (IcomNetworkIfBlock block in speakerPcm.Push(
                   sampleRate, packet, DateTime.UtcNow))
          speakers.AddSamples(block.Samples);
      }
    }

    private async Task FlushSpeakerAsync(CancellationToken token)
    {
      using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
      try
      {
        while (await timer.WaitForNextTickAsync(token))
        {
          lock (speakerSync)
          {
            if (speakers == null) break;
            foreach (IcomNetworkIfBlock block in speakerPcm.Flush(DateTime.UtcNow))
              speakers.AddSamples(block.Samples);
          }
        }
      }
      catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
      if (disposed) return;
      disposed = true;
      microphone?.SamplesAvailable -= OnMicrophoneSamples;
      microphone?.Dispose();
      microphone = null;
      stop?.Cancel();
      lock (speakerSync)
      {
        speakers?.Dispose();
        speakers = null;
        speakerPcm.Reset();
      }
      stop?.Dispose();
      stop = null;
      Status = "Audio endpoints stopped";
    }
  }
}
