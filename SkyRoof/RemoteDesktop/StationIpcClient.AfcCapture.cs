using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    private StationAfcCapture.StationAfcCaptureClient AfcCapture => new(channel);
    internal Task<AfcCaptureStatus> StartStationAfcAsync(AfcCaptureStart request, CancellationToken token) =>
        AfcCapture.StartAsync(request, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;
    internal Task<AfcCaptureStatus> StationAfcAsync(CancellationToken token) =>
        AfcCapture.StatusAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
    internal Task<AfcCaptureStatus> RetargetStationAfcAsync(AfcCaptureTarget request, CancellationToken token) =>
        AfcCapture.RetargetAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
    internal Task<AfcCaptureStatus> StopStationAfcAsync(CancellationToken token) =>
        AfcCapture.StopAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;
}
