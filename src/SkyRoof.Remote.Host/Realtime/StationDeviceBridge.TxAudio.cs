using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationDeviceBridge
{
    public async Task<RemoteTxPcmReceipt> PushTxPcmAsync(string sessionId, string transactionId,
        ulong epoch, ulong sequence, byte[] pcm, CancellationToken token)
    {
        var result = await Client.PushTxPcmAsync(new TxPcmChunk
        {
            SessionId = sessionId, TransactionId = transactionId, Epoch = epoch,
            Sequence = sequence, Pcm16 = Google.Protobuf.ByteString.CopyFrom(pcm)
        }, deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: token);
        return new(result.Accepted, result.State);
    }
}
