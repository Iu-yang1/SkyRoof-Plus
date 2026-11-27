using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;
using Grpc.Core;

namespace SkyRoof.Remote.Station;

// Station owns the recording lifetime, independently of the requesting IPC client.
public sealed class StationRecordingWorker : BackgroundService
{
    private readonly ILogger<StationRecordingWorker> logger;
    public StationRecordingWorker(ILogger<StationRecordingWorker> logger) => this.logger = logger;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateGate = new();
    private StationRecordingStatus state = new() { Detail = "Idle" };
    private CancellationTokenSource? capture;
    private Task? recording;

    internal StationRecordingStatus Snapshot() { lock (stateGate) return state.Clone(); }

    internal static bool IsRecordingKind(StationSignalKind kind) => kind is StationSignalKind.StationSignalRxAudio or StationSignalKind.StationSignalIqCf32 or StationSignalKind.StationSignalRawIqCf32;

    internal async Task<StationRecordingStatus> BeginAsync(StationSignalKind kind, bool resume, CancellationToken token)
    {
        if (!IsRecordingKind(kind))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Select AF, channelized IQ or raw SDR IQ."));
        await gate.WaitAsync(token);
        try
        {
            if (recording is { IsCompleted: false })
                throw new RpcException(new Status(StatusCode.AlreadyExists, "Station recording already active."));
            Directory.CreateDirectory(StationRecordingFiles.DirectoryPath);
            var intent = new StationRecordingIntent(Guid.NewGuid().ToString("N"), kind);
            if (resume) intent.Save();
            else StationRecordingIntent.Clear();
            Launch(intent, resume, false);
            return Snapshot();
        }
        finally { gate.Release(); }
    }

    private void Launch(StationRecordingIntent intent, bool resume, bool restored)
    {
        capture?.Dispose();
        // A distinct physical segment is mandatory even for the same durable task ID.
        string file = Path.Combine(StationRecordingFiles.DirectoryPath,
            $"skyroof-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wav");
        lock (stateGate) state = new() { Id = intent.Id, Kind = intent.Kind, Running = true,
            ResumeAfterRestart = resume, Detail = restored ? "Resumed; waiting for signal" : "Waiting for signal" };
        capture = new CancellationTokenSource();
        var ct = capture.Token;
        recording = Task.Run(() => RecordAsync(intent.Kind, file, ct), CancellationToken.None);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (StationRecordingIntent.Load() is { } intent)
                Launch(intent, true, true);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            lock (stateGate) state = new() { Detail = "Recording restore failed: " + error.Message };
            logger.LogError(error, "Recording intent could not be restored");
        }
        return base.StartAsync(cancellationToken);
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

    internal async Task<StationRecordingStatus> EndAsync(CancellationToken token, bool preserveIntent = false)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!preserveIntent && Directory.Exists(StationRecordingFiles.DirectoryPath)) StationRecordingIntent.Clear();
            capture?.Cancel();
            if (recording is not null) await recording.WaitAsync(token);
            capture?.Dispose();
            capture = null;
            recording = null;
            if (!preserveIntent) lock (stateGate) state.ResumeAfterRestart = false;
            return Snapshot();
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await EndAsync(CancellationToken.None, preserveIntent: true); }
    }
}

public sealed partial class StationRecorderService(StationRecordingWorker worker) : StationRecorder.StationRecorderBase
{
    public override Task<StationRecordingStatus> Start(StationRecordingStart request, ServerCallContext context) =>
        worker.BeginAsync(request.Kind, request.ResumeAfterRestart, context.CancellationToken);
    public override Task<StationRecordingStatus> Status(StationRecordingQuery request, ServerCallContext context) =>
        Task.FromResult(worker.Snapshot());
    public override Task<StationRecordingStatus> Stop(StationRecordingQuery request, ServerCallContext context) =>
        worker.EndAsync(context.CancellationToken);
}
