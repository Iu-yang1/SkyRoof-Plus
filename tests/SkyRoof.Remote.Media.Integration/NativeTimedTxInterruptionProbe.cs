using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

internal static class NativeTimedTxInterruptionProbe
{
    public static async Task RunAsync(StationDevice.StationDeviceClient cat, CancellationToken ct)
    {
        await CancelKeyedAsync(cat, ct);
        await ManualTakeoverAsync(cat, ct);
        Console.WriteLine("PASS: keyed timed TX cancellation releases RX; manual takeover survives old job cleanup.");
    }

    private static TimedTxRequest Request()
    {
        byte[] pcm = new byte[48000 * 2 * 3];
        return new TimedTxRequest
        {
            JobId = Guid.NewGuid().ToString("N"),
            SessionId = "timed-interrupt", AuthorizedPrincipal = "operator",
            TargetUtcNanoseconds = (DateTime.UtcNow.AddSeconds(2).Ticks - DateTime.UnixEpoch.Ticks) * 100,
            FrequencyHz = 435_550_000, Mode = "USB-D",
            Pcm16 = ByteString.CopyFrom(pcm), Sha256 = Convert.ToHexString(SHA256.HashData(pcm))
        };
    }

    private static async Task CancelKeyedAsync(StationDevice.StationDeviceClient cat, CancellationToken ct)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var request = Request();
        using var call = cat.RunTimedTxAsync(request, deadline: DateTime.UtcNow.AddSeconds(10),
            cancellationToken: cancel.Token);
        await WaitForAsync(cat, snapshot => snapshot.ActualPtt &&
            snapshot.ActiveTransactionId == request.JobId, ct);
        cancel.Cancel();
        try
        {
            await call.ResponseAsync;
            throw new InvalidOperationException("Cancelled timed TX unexpectedly completed.");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
        await WaitForAsync(cat, snapshot => !snapshot.ActualPtt && snapshot.ActiveTransactionId.Length == 0, ct);
    }

    private static async Task ManualTakeoverAsync(StationDevice.StationDeviceClient cat, CancellationToken ct)
    {
        var request = Request();
        using var call = cat.RunTimedTxAsync(request, deadline: DateTime.UtcNow.AddSeconds(10),
            cancellationToken: ct);
        await WaitForAsync(cat, snapshot => snapshot.ActualPtt &&
            snapshot.ActiveTransactionId == request.JobId, ct);
        var manual = new DeviceCommand
        {
            Operation = DeviceOperation.DeviceManualBegin, TransactionId = "timed-manual-takeover",
            SessionId = "manual-operator", FrequencyHz = request.FrequencyHz, Mode = request.Mode, AudioSource = "LAN"
        };
        try
        {
            var active = await cat.ExecuteAsync(manual, cancellationToken: ct);
            if (!active.ActualPtt || active.ActiveTransactionId != manual.TransactionId)
                throw new InvalidOperationException("Manual PTT did not take ownership of timed TX.");
            try
            {
                await call.ResponseAsync;
                throw new InvalidOperationException("Preempted timed TX unexpectedly completed.");
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition) { }
            var after = await cat.ReadbackAsync(new(), cancellationToken: ct);
            if (!after.ActualPtt || after.ActiveTransactionId != manual.TransactionId)
                throw new InvalidOperationException("Old timed job released the new manual owner.");
        }
        finally
        {
            manual.Operation = DeviceOperation.DeviceManualEnd;
            await cat.ExecuteAsync(manual, cancellationToken: ct);
            await cat.ExecuteAsync(new DeviceCommand { Operation = DeviceOperation.DeviceResumeDigitalTx },
                cancellationToken: ct);
        }
    }

    private static async Task WaitForAsync(StationDevice.StationDeviceClient cat,
        Func<DeviceSnapshot, bool> predicate, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        while (true)
        {
            var snapshot = await cat.ReadbackAsync(new(), cancellationToken: timeout.Token);
            if (predicate(snapshot)) return;
            await Task.Delay(30, timeout.Token);
        }
    }
}
