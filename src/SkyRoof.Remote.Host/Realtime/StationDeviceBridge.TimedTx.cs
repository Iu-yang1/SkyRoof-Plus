using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationDeviceBridge
{
    public async Task<TimedTxReceipt> RunTimedTxAsync(TimedTxRequest request, CancellationToken ct)
    {
        var target = DateTime.UnixEpoch.AddTicks(request.TargetUtcNanoseconds / 100);
        var duration = TimeSpan.FromSeconds(request.Pcm16.Length / 96000.0);
        return await Client.RunTimedTxAsync(request,
            deadline: target.Add(duration).AddSeconds(15), cancellationToken: ct);
    }
}
