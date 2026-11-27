using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationDeviceBridge
{
    private StationSdr.StationSdrClient Sdr => new(_channel.Value);
    public Task<SdrStatus> ReadSdrAsync(CancellationToken token) =>
        Sdr.ReadAsync(new SdrStatusRequest(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;
    public Task<SdrStatus> StartSdrAsync(SdrCaptureRequest request, CancellationToken token) =>
        Sdr.StartAsync(request, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: token).ResponseAsync;
    public Task<SdrStatus> StopSdrAsync(CancellationToken token) =>
        Sdr.StopAsync(new SdrStatusRequest(), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: token).ResponseAsync;
}
