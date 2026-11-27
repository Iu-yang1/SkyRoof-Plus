using Grpc.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SkyRoof.AFC;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class StationAfcCaptureWorker : BackgroundService
{
    private readonly TrackingJobRunner runner;
    private readonly ILogger<StationAfcCaptureWorker> logger;

    public StationAfcCaptureWorker(TrackingJobRunner runner, ILogger<StationAfcCaptureWorker> logger)
    {
        this.runner = runner;
        this.logger = logger;
    }

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateGate = new();
    private AfcCaptureStatus state = new() { Detail = "Idle" };
    private CancellationTokenSource? capture;
    private Task? worker;
    private AfcCaptureTarget? pending;
    internal AfcCaptureStatus Snapshot() { lock (stateGate) return state.Clone(); }

    private static AfcAcquisitionPreset Validate(AfcCaptureTarget target)
    {
        if (!double.IsFinite(target.TargetHz) || Math.Abs(target.TargetHz) > 6000 ||
            (target.HasCenterHz && (!double.IsFinite(target.CenterHz) || Math.Abs(target.CenterHz) > 6000)) ||
            !Enum.TryParse<AfcAcquisitionPreset>(target.Preset, true, out var preset) || !Enum.IsDefined(preset))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid IF target or acquisition preset."));
        return preset;
    }

    internal async Task<AfcCaptureStatus> BeginAsync(AfcCaptureStart request, CancellationToken token)
    {
        if (request.Control is null || request.Target is null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "AFC profile and IF target required."));
        Validate(request.Target);
        string? endpoint = Environment.GetEnvironmentVariable("SKYROOF_AFC_IF_DEVICE");
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Configure the station's actual 12 kHz IF endpoint (SKYROOF_AFC_IF_DEVICE)."));
        await gate.WaitAsync(token);
        try
        {
            if (worker is { IsCompleted: false })
                throw new RpcException(new Status(StatusCode.AlreadyExists, "Station IF capture already running."));
            capture?.Dispose();
            var opened = await runner.OpenAfcAsync(request.Control, token);
            capture = new CancellationTokenSource();
            var ct = capture.Token;
            lock (stateGate) { pending = null; state = new() { Running = true, Detail = "Opening station IF", Afc = opened }; }
            worker = Task.Run(() => CaptureAsync(endpoint, request.Clone(), opened.SessionId, ct), CancellationToken.None);
            return Snapshot();
        }
        finally { gate.Release(); }
    }

    internal AfcCaptureStatus Retarget(AfcCaptureTarget target)
    {
        Validate(target);
        lock (stateGate)
        {
            if (!state.Running) throw new RpcException(new Status(StatusCode.FailedPrecondition, "Station IF capture is not running."));
            pending = target.Clone();
            return state.Clone();
        }
    }

    private async Task CaptureAsync(string endpoint, AfcCaptureStart request, string session, CancellationToken token)
    {
        string detail = "Stopped";
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(endpoint);
            bool loopback = string.Equals(Environment.GetEnvironmentVariable("SKYROOF_AFC_IF_LOOPBACK"), "true", StringComparison.OrdinalIgnoreCase);
            int channel = string.Equals(Environment.GetEnvironmentVariable("SKYROOF_AFC_IF_CHANNEL"), "right", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            using WasapiCapture input = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
            if (input.WaveFormat.SampleRate != 48000 || channel >= input.WaveFormat.Channels)
                throw new InvalidDataException("Set station IF endpoint to 48 kHz with the selected channel.");
            var buffer = new StationIfSamples();
            var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            input.DataAvailable += (_, e) =>
            {
                try { buffer.Accept(e.Buffer.AsSpan(0, e.BytesRecorded), input.WaveFormat, channel, DateTime.UtcNow); }
                catch (Exception ex) { stopped.TrySetResult(ex); }
            };
            input.RecordingStopped += (_, e) => stopped.TrySetResult(e.Exception);
            input.StartRecording();
            try { await RunAsync(buffer, request, session, stopped.Task, token); }
            finally { input.StopRecording(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { detail = ex.Message; logger.LogWarning(ex, "Station IF capture ended"); }
        finally
        {
            try
            {
                var closed = await runner.CloseAfcAsync(session, CancellationToken.None);
                lock (stateGate) state.Afc = closed;
            }
            finally
            {
                lock (stateGate) { state.Running = false; state.Detail = detail; pending = null; }
            }
        }
    }

    private async Task RunAsync(StationIfSamples buffer, AfcCaptureStart request, string session, Task<Exception?> stopped, CancellationToken token)
    {
        var mode = Enum.Parse<AfcSignalMode>(request.Control.Mode, true);
        var target = request.Target;
        var processor = new AfcSignalProcessing();
        while (!token.IsCancellationRequested)
        {
            if (stopped.IsCompleted)
                throw new IOException("Station IF source stopped.", await stopped);
            var update = new AfcUpdateRequest { SessionId = session };
            AfcCaptureTarget? change;
            lock (stateGate) { change = pending; pending = null; }
            if (change is not null)
            {
                target = change;
                var generation = buffer.Reset();
                processor = new AfcSignalProcessing();
                update.Target = new AfcTargetChange { Generation = generation };
                if (target.HasCenterHz) update.Target.CenterHz = target.CenterHz;
            }
            var preset = Validate(target);
            var profile = AfcTrackingProfile.For(mode, target.SsbPilot, preset);
            if (update.Target is null && buffer.Take(profile, DateTime.UtcNow) is { } frame)
            {
                var obs = processor.Analyze(frame.Pcm, mode, frame.Utc, frame.Generation, target.TargetHz, target.SsbPilot, preset);
                update.Measurement = new AfcMeasurement
                {
                    SampleUtcMilliseconds = new DateTimeOffset(obs.SampleUtc).ToUnixTimeMilliseconds(), Generation = obs.Generation,
                    IfOffsetHz = obs.IfOffsetHz, SigmaHz = obs.SigmaHz, Confidence = obs.Confidence,
                    AbsoluteReference = obs.HasAbsoluteReference, Ambiguous = obs.Ambiguous
                };
            }
            var status = await runner.UpdateAfcAsync(update, token);
            lock (stateGate)
            {
                state.Afc = status;
                state.Samples = buffer.Samples;
                state.Detail = update.Measurement is null ? "Waiting for fresh IF window" : "Station IF active";
            }
            await Task.Delay(profile.CadenceMilliseconds, token);
        }
    }

    internal async Task<AfcCaptureStatus> EndAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            capture?.Cancel();
            if (worker is not null) await worker.WaitAsync(token);
            capture?.Dispose(); capture = null; worker = null;
            return Snapshot();
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await EndAsync(CancellationToken.None); }
    }
}
