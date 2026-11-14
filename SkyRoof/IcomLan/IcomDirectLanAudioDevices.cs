using System.Threading.Channels;
using NAudio.CoreAudioApi;
using SkyRoof.IF;
using VE3NEA;
using Serilog;

namespace SkyRoof
{
  /// <summary>
  /// Dedicated bounded audio worker. UDP receive/Scope/CAT threads must not
  /// run WASAPI initialization, IF demodulation or audio render writes.
  /// </summary>
  internal sealed class IcomDirectLanAudioDevices : IDisposable
  {
    private readonly IcomDirectLanSettings options;
    private readonly IcomLanSpectrumCapture capture;
    private readonly Action<int, IcomLanLpcmAudioCodec.RxFrame>? ifAudioConsumer;
    private readonly IcomNetworkIfPcmPipeline speakerPcm = new();
    private readonly object deviceSync = new();
    private readonly Channel<(int Rate, IcomLanLpcmAudioCodec.RxFrame Frame)> receiveQueue =
      Channel.CreateBounded<(int, IcomLanLpcmAudioCodec.RxFrame)>(
        new BoundedChannelOptions(24)
        {
          SingleWriter = false,
          SingleReader = true,
          FullMode = BoundedChannelFullMode.DropOldest
        });
    private readonly CancellationTokenSource lifetime = new();
    private InputSoundcard<float>? microphone;
    private OutputSoundcard<float>? speakers;
    private int disposed;
    private readonly Task priorEndpointShutdown;
    private Task? endpointInitialization;
    private Task endpointShutdown = Task.CompletedTask;
    internal Task EndpointShutdown => endpointShutdown;
    private long queuedRx;
    private long renderedRx;
    internal string Status { get; private set; } = "Audio endpoints not started";
    internal long QueuedRxFrames => Interlocked.Read(ref queuedRx);
    internal long RenderedRxFrames => Interlocked.Read(ref renderedRx);

    internal IcomDirectLanAudioDevices(
      IcomDirectLanSettings options, IcomLanSpectrumCapture capture,
      Action<int, IcomLanLpcmAudioCodec.RxFrame>? ifAudioConsumer = null,
      Task? priorEndpointShutdown = null)
    {
      this.options = options;
      this.priorEndpointShutdown = priorEndpointShutdown ?? Task.CompletedTask;
      this.capture = capture;
      this.ifAudioConsumer = ifAudioConsumer;
    }

    internal void Start()
    {
      if (Volatile.Read(ref disposed) != 0)
        throw new ObjectDisposedException(nameof(IcomDirectLanAudioDevices));

      // Starting a WASAPI endpoint may perform blocking COM operations.
      // Neither the WinForms UI nor any UDP scope/audio receive callback
      // should wait for endpoint initialization.
      endpointInitialization = Task.Run(async () =>
      {
        await priorEndpointShutdown.WaitAsync(lifetime.Token);
        if (Volatile.Read(ref disposed) == 0)
          InitializeEndpoints();
      }, lifetime.Token);
      _ = Task.Run(() => ReceiveLoopAsync(lifetime.Token), lifetime.Token);
      _ = Task.Run(() => FlushSpeakerAsync(lifetime.Token), lifetime.Token);
    }

    private void InitializeEndpoints()
    {
      var notes = new List<string>();
      if (options.Receive.Enabled)
      {
        try
        {
          string? endpoint = options.Receive.SpeakerDeviceId ??
            Soundcard.GetDefaultSoundcardId(DataFlow.Render);
          if (endpoint == null)
            throw new InvalidOperationException("No Windows speaker available");
          var output = new OutputSoundcard<float>(
            endpoint, samplingRate: 48000,
            latencyMilliseconds: Math.Clamp(options.Receive.PrebufferMs, 20, 500),
            padSilenceOnUnderrun: true);
          output.Volume = Math.Clamp(options.Receive.SpeakerVolume, 0, 100) / 100f;
          output.Enabled = true;
          if (!output.IsRunning)
            throw new InvalidOperationException("Selected Windows speaker did not start");
          lock (deviceSync)
          {
            if (disposed == 0) speakers = output;
            else output.Dispose();
          }
          notes.Add("RX speaker active");
        }
        catch (Exception ex)
        {
          notes.Add("RX speaker unavailable");
          Log.Warning(ex, "Direct LAN RX speaker initialization failed");
        }
      }
      else notes.Add("RX audio disabled");

      if (options.Transmit.Enabled && Volatile.Read(ref disposed) == 0)
      {
        try
        {
          string? endpoint = options.Transmit.MicrophoneDeviceId ??
            Soundcard.GetDefaultSoundcardId(DataFlow.Capture);
          if (endpoint == null)
            throw new InvalidOperationException("No Windows microphone available");
          int rate = (int)options.Transmit.SampleRate;
          var input = new InputSoundcard<float>(
            endpoint, rate,
            captureBufferMilliseconds: Math.Clamp(options.Transmit.CaptureBufferMs, 20, 500),
            readerBlockSizeSamples: Math.Max(128, rate / 50),
            idleSleepMilliseconds: 2);
          input.SamplesAvailable += OnMicrophoneSamples;
          input.Enabled = true;
          if (!input.IsRunning)
          {
            input.SamplesAvailable -= OnMicrophoneSamples;
            input.Dispose();
            throw new InvalidOperationException("Selected Windows microphone did not start");
          }
          lock (deviceSync)
          {
            if (disposed == 0) microphone = input;
            else
            {
              input.SamplesAvailable -= OnMicrophoneSamples;
              input.Dispose();
            }
          }
          notes.Add("TX microphone active (CAT PTT required)");
        }
        catch (Exception ex)
        {
          notes.Add("TX microphone unavailable");
          Log.Warning(ex, "Direct LAN TX microphone initialization failed");
        }
      }
      else notes.Add("TX audio disabled");

      if (Volatile.Read(ref disposed) == 0)
      {
        Status = string.Join("; ", notes);
        Log.Information("Direct LAN WASAPI setup: {Status}", Status);
      }
    }

    private void OnMicrophoneSamples(object? sender, DataEventArgs<float> args)
    {
      if (Volatile.Read(ref disposed) == 0 && args.Count > 0)
        capture.TryQueueTransmitAudio(args.Data, args.Count);
    }

    internal void AcceptReceiveAudio(
      int sampleRate, IcomLanLpcmAudioCodec.RxFrame packet)
    {
      if (Volatile.Read(ref disposed) != 0) return;
      if (receiveQueue.Writer.TryWrite((sampleRate, packet)))
        Interlocked.Increment(ref queuedRx);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
      try
      {
        await foreach (var item in receiveQueue.Reader.ReadAllAsync(token))
        {
          try
          {
            ifAudioConsumer?.Invoke(item.Rate, item.Frame);
            lock (deviceSync)
            {
              if (speakers == null) continue;
              foreach (IcomNetworkIfBlock block in speakerPcm.Push(
                         item.Rate, item.Frame, DateTime.UtcNow))
              {
                speakers.AddSamples(block.Samples);
                Interlocked.Increment(ref renderedRx);
              }
            }
          }
          catch (Exception ex)
          {
            Log.Warning(ex, "Direct LAN audio consumer failed (UDP scope unaffected)");
          }
        }
      }
      catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task FlushSpeakerAsync(CancellationToken token)
    {
      using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
      try
      {
        while (await timer.WaitForNextTickAsync(token))
        {
          lock (deviceSync)
          {
            if (speakers == null) continue;
            foreach (IcomNetworkIfBlock block in speakerPcm.Flush(DateTime.UtcNow))
              speakers.AddSamples(block.Samples);
          }
        }
      }
      catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
      if (Interlocked.Exchange(ref disposed, 1) != 0) return;
      lifetime.Cancel();
      receiveQueue.Writer.TryComplete();

      InputSoundcard<float>? oldInput;
      OutputSoundcard<float>? oldOutput;
      lock (deviceSync)
      {
        oldInput = microphone;
        oldOutput = speakers;
        microphone = null;
        speakers = null;
        speakerPcm.Reset();
      }
      if (oldInput != null)
        oldInput.SamplesAvailable -= OnMicrophoneSamples;
      // Stopping WASAPI can block on its reader thread: never block the
      // WinForms UI while closing a spectrum/audio session.
      Task initialization = endpointInitialization ?? Task.CompletedTask;
      endpointShutdown = Task.Run(async () =>
      {
        try { await initialization; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Log.Warning(ex, "Direct LAN audio endpoint startup failed during shutdown"); }
        try { oldInput?.Dispose(); }
        catch (Exception ex) { Log.Warning(ex, "Direct LAN microphone shutdown failed"); }
        try { oldOutput?.Dispose(); }
        catch (Exception ex) { Log.Warning(ex, "Direct LAN speaker shutdown failed"); }
      });
      Status = "Audio endpoints stopped";
    }
  }
}
