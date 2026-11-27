using Microsoft.AspNetCore.SignalR;

namespace SkyRoof.Remote.Host.Realtime;

public sealed partial class StationControlHub
{
    // The connection ID is server-derived, never supplied by the browser.
    public async Task<RemoteTxPcmReceipt> PushTxPcm(string transactionId, ulong epoch, ulong sequence, byte[] pcm)
    {
        if (pcm.Length != 1920) throw new HubException("Expected 20 ms PCM16.");
        await VerifyCurrentSessionAsync(requireOperator: true);
        return await device.PushTxPcmAsync(Context.ConnectionId, transactionId, epoch, sequence,
            pcm, Context.ConnectionAborted);
    }
}
