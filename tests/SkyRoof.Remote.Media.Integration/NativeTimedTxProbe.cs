using System.Buffers.Binary;
using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

internal static class NativeTimedTxProbe
{
    public static async Task RunAsync(StationDevice.StationDeviceClient cat,
        FakeNativeIcomRadio fake, CancellationToken ct)
    {
        const int frames = 10;
        byte[] pcm = new byte[frames * 1920];
        for (int i = 0; i < pcm.Length / 2; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(6000 + i % 960));
        long target = (DateTime.UtcNow.AddSeconds(2).Ticks - DateTime.UnixEpoch.Ticks) * 100;
        var request = new TimedTxRequest
        {
            JobId = Guid.NewGuid().ToString("N"), SessionId = "timed-test",
            AuthorizedPrincipal = "operator", TargetUtcNanoseconds = target,
            FrequencyHz = 435_550_000, Mode = "USB-D", Pcm16 = ByteString.CopyFrom(pcm),
            Sha256 = Convert.ToHexString(SHA256.HashData(pcm))
        };
        var receipt = await cat.RunTimedTxAsync(request, deadline: DateTime.UtcNow.AddSeconds(10),
            cancellationToken: ct);
        if (receipt.State != "completed" || receipt.SourceSamples != frames * 960 ||
            Math.Abs(receipt.ActualFirstPacketUtcNanoseconds - target) > 80_000_000)
            throw new InvalidDataException("Timed TX did not meet the first UDP packet deadline.");
        await fake.WaitForTxSampleAsync(6959, ct);
        if (!fake.TimedSamples.SequenceEqual(Enumerable.Range(0, frames * 960).Select(i => (short)(6000 + i % 960))))
            throw new InvalidDataException("Timed waveform samples were dropped or reordered.");
        var after = await cat.ReadbackAsync(new(), cancellationToken: ct);
        if (after.ActualPtt || after.ActiveTransactionId.Length != 0)
            throw new InvalidDataException("Timed TX failed to release PTT.");
        request.TargetUtcNanoseconds = (DateTime.UtcNow.AddSeconds(2).Ticks - DateTime.UnixEpoch.Ticks) * 100;
        try
        {
            await cat.RunTimedTxAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
            throw new InvalidDataException("Sealed job ID was replayed.");
        }
        catch (RpcException error) when (error.StatusCode is StatusCode.Unavailable or StatusCode.FailedPrecondition)
        { Console.WriteLine("PASS: sealed timed job cannot replay."); }
        Console.WriteLine("PASS: timed PCM IPC -> local monotonic clock -> exact native UDP waveform -> verified RX.");
    }
}
