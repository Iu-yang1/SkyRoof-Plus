using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    private StationRecorder.StationRecorderClient Recorder => new(channel);
    internal Task<StationRecordingStatus> StartStationRecordingAsync(StationSignalKind kind, CancellationToken token, bool resumeAfterRestart = false) =>
        Recorder.StartAsync(new StationRecordingStart { Kind = kind, ResumeAfterRestart = resumeAfterRestart }, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;
    internal Task<StationRecordingStatus> StationRecordingAsync(CancellationToken token) =>
        Recorder.StatusAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
    internal Task<StationRecordingStatus> StopStationRecordingAsync(CancellationToken token) =>
        Recorder.StopAsync(new(), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: token).ResponseAsync;
}
