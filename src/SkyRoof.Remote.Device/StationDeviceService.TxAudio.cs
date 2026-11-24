using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    public override async Task<TxPcmReceipt> PushTxPcm(TxPcmChunk request, ServerCallContext context)
    {
        if (request.Pcm16.Length != 1920)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Expected 20 ms of mono 48 kHz PCM16."));
        await _commands.WaitAsync(context.CancellationToken);
        try
        {
            if (_owner is not { } owner || owner.Epoch != request.Epoch ||
                owner.SessionId != request.SessionId || owner.TransactionId != request.TransactionId ||
                !owner.AudioSource.Equals("LAN", StringComparison.OrdinalIgnoreCase))
                return new() { State = "stale-or-not-owner" };
            if (cat is not NativeIcomCatBackend native || native.Tx is not { } audio)
                return new() { State = "no-native-tx-grant" };
            audio.Select(owner.Epoch);
            bool accepted = audio.Push(request.Sequence, request.Pcm16.Span);
            return new() { Accepted = accepted, State = accepted ? "queued" : "duplicate" };
        }
        finally { _commands.Release(); }
    }

    internal async Task TickNativeTxAsync(CancellationToken ct)
    {
        if (!await _commands.WaitAsync(0, ct)) return;
        try
        {
            if (cat is not NativeIcomCatBackend native || native.Tx is not { } audio) return;
            if (_owner is not { } owner || !owner.AudioSource.Equals("LAN", StringComparison.OrdinalIgnoreCase))
            {
                audio.Select(0);
                return;
            }
            audio.Select(owner.Epoch);
            await audio.TickAsync(ct);
        }
        finally { _commands.Release(); }
    }
}
