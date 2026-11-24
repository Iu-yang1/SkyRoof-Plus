using System.Buffers.Binary;
using Google.Protobuf;
using SkyRoof.Remote.Ipc;

internal static class NativeTxOwnershipProbe
{
    public static async Task RunAsync(StationDevice.StationDeviceClient cat, FakeNativeIcomRadio fake,
        DeviceCommand begin, DeviceSnapshot asserted, CancellationToken ct)
    {
        var first = Chunk(begin, asserted.TransactionEpoch, 0, 1234);
        await FeedAsync(cat, first, ct);
        await fake.WaitForTxSampleAsync(1234, ct);
        var duplicate = await cat.PushTxPcmAsync(first, cancellationToken: ct);
        if (duplicate.Accepted) throw new InvalidOperationException("Duplicate TX PCM accepted.");

        var takeover = begin.Clone();
        takeover.TransactionId = "native-takeover";
        takeover.SessionId = "operator-2";
        var active = await cat.ExecuteAsync(takeover, cancellationToken: ct);
        if (!active.ActualPtt || active.TransactionEpoch == asserted.TransactionEpoch ||
            active.StatusText != "source-takeover-no-rf-transition")
            throw new InvalidOperationException("Native TX takeover did not retain RF and advance epoch.");
        first.Sequence = 99;
        if ((await cat.PushTxPcmAsync(first, cancellationToken: ct)).Accepted)
            throw new InvalidOperationException("Previous owner submitted PCM after takeover.");
        await FeedAsync(cat, Chunk(takeover, active.TransactionEpoch, 0, -2345), ct);
        await fake.WaitForTxSampleAsync(-2345, ct);
        Console.WriteLine("PASS: TX PCM crosses real Device IPC and native UDP; old owner and duplicate blocked.");
    }

    public static async Task AssertRejectedAsync(StationDevice.StationDeviceClient cat,
        DeviceCommand command, ulong epoch, CancellationToken ct)
    {
        if ((await cat.PushTxPcmAsync(Chunk(command, epoch, 0, 9999), cancellationToken: ct)).Accepted)
            throw new InvalidOperationException("TX PCM accepted while RX.");
    }

    private static async Task FeedAsync(StationDevice.StationDeviceClient cat, TxPcmChunk chunk,
        CancellationToken ct)
    {
        // Two concurrent browser RPCs can complete in reverse order.
        foreach (ulong sequence in new ulong[] { 0, 2, 1, 4, 3 })
        {
            chunk.Sequence = sequence;
            if (!(await cat.PushTxPcmAsync(chunk, cancellationToken: ct)).Accepted)
                throw new InvalidOperationException("Owned TX PCM rejected.");
        }
    }

    private static TxPcmChunk Chunk(DeviceCommand owner, ulong epoch, ulong sequence, short sample)
    {
        var bytes = new byte[1920];
        for (int i = 0; i < bytes.Length; i += 2)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i, 2), sample);
        return new() { TransactionId = owner.TransactionId, SessionId = owner.SessionId,
            Epoch = epoch, Sequence = sequence, Pcm16 = ByteString.CopyFrom(bytes) };
    }
}
