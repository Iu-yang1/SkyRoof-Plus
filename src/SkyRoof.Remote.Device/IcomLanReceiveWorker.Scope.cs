using System.Diagnostics;
using System.Security.Cryptography;
using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Device;

internal sealed partial class IcomLanReceiveWorker
{
    private static async Task RunScopeToBusAsync(IcomLanCivSocket civ, CancellationToken ct)
    {
        var scope = civ.RfScope;
        if (scope is null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return;
        }
        string name = Environment.GetEnvironmentVariable("SKYROOF_RF_SCOPE_BUS") ?? "skyroof-rf-scope";
        ulong epoch = (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        using var main = new NamedSignalRing(name + "-main", 64, 4096, epoch);
        using var sub = new NamedSignalRing(name + "-sub", 64, 4096, epoch);
        IcomLanRfScopeFrame? previousMain = null, previousSub = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
        while (await timer.WaitForNextTickAsync(ct))
        {
            PublishLatest(main, scope.Latest(false), ref previousMain);
            PublishLatest(sub, scope.Latest(true), ref previousSub);
        }
    }

    private static void PublishLatest(NamedSignalRing ring, IcomLanRfScopeFrame? latest,
        ref IcomLanRfScopeFrame? previous)
    {
        if (latest is null || ReferenceEquals(latest, previous)) return;
        previous = latest;
        ring.Publish(0, Stopwatch.GetTimestamp(), checked((int)(latest.UpperHz - latest.LowerHz)),
            IcomLanRfScopeFrame.Points, latest.Encode());
    }
}
