using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;
using Grpc.Core;

namespace SkyRoof.Remote.Station;

// Station owns the recording lifetime, independently of the requesting IPC client.
public sealed class StationRecordingWorker(ILogger<StationRecordingWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateGate = new();
    private StationRecordingStatus state = new() { Detail = "Idle" };
    private CancellationTokenSource? capture;
    private Task? recording;

    internal StationRecordingStatus Snapshot() { lock (stateGate) return state.Clone(); }

    internal async Task<StationRecordingStatus> BeginAsync(StationSignalKind kind, CancellationToken token)
    {
        if (kind is not (StationSignalKind.StationSignalRxAudio or StationSignalKind.StationSignalIqCf32 or StationSignalKind.StationSignalRawIqCf32))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Select AF, channelized IQ or raw SDR IQ."));
        await gate.WaitAsync(token);
        try
        {
            if (recording is { IsCompleted: false })
                throw new RpcException(new Status(StatusCode.AlreadyExists, "Station recording already active."));
            capture?.Dispose();
            var root = Environment.GetEnvironmentVariable("SKYROOF_RECORDING_DIRECTORY") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyRoof", "Remote", "recordings");
            Directory.CreateDirectory(root);
            var id = Guid.NewGuid().ToString("N");
            string file = Path.Combine(root, $"skyroof-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{id}.wav");
            lock (stateGate) state = new() { Id = id, Kind = kind, Running = true, Detail = "Waiting for signal" };
            capture = new CancellationTokenSource();
            var ct = capture.Token;
            recording = Task.Run(() => RecordAsync(kind, file, ct), CancellationToken.None);
            return Snapshot();
        }
        finally { gate.Release(); }
    }

    private async Task RecordAsync(StationSignalKind kind, string file, CancellationToken ct)
    {
        string detail = "Stopped";
        try
        {
            using var writer = new RemoteRecordingWriter(file, kind != StationSignalKind.StationSignalRxAudio);
            await foreach (var frame in StationSignalService.ReadFramesAsync(kind, ct))
            {
                writer.Accept(frame);
                lock (stateGate)
                {
                    state.Samples = writer.Samples;
                    state.DurationSeconds = writer.DurationSeconds;
                    state.Discontinuities = writer.Discontinuities;
                    if (state.Files.Count != writer.Files.Count)
                    {
                        state.Files.Clear();
                        state.Files.AddRange(writer.Files.Select(Path.GetFileName).OfType<string>());
                    }
                    state.Detail = "Recording";
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            detail = "Recording failed: " + ex.Message;
            logger.LogError(ex, "Station recording failed");
        }
        finally { lock (stateGate) { state.Running = false; state.Detail = detail; } }
    }

    internal async Task<StationRecordingStatus> EndAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            capture?.Cancel();
            if (recording is not null) await recording.WaitAsync(token);
            capture?.Dispose();
            capture = null;
            recording = null;
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

public sealed class StationRecorderService(StationRecordingWorker worker) : StationRecorder.StationRecorderBase
{
    public override Task<StationRecordingStatus> Start(StationRecordingStart request, ServerCallContext context) =>
        worker.BeginAsync(request.Kind, context.CancellationToken);
    public override Task<StationRecordingStatus> Status(StationRecordingQuery request, ServerCallContext context) =>
        Task.FromResult(worker.Snapshot());
    public override Task<StationRecordingStatus> Stop(StationRecordingQuery request, ServerCallContext context) =>
        worker.EndAsync(context.CancellationToken);
}
