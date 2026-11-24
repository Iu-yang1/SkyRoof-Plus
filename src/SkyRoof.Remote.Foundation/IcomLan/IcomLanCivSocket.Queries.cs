using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanCivSocket
{
    private int _civInnerSequence;

    // The maintenance loop is the only UDP receiver. Query callers never
    // compete with keepalive or future spectrum delivery for ReceiveAsync.
    public Task<byte[]> QueryAsync(IcomCivQuery query, CancellationToken ct = default)
    {
        if (!_opened) throw new InvalidOperationException("CI-V stream is not open.");
        return _queries.ExecuteAsync(query,
            (frame, token) => SendTrackedAsync(
                IcomLanCivPacketCodec.Encode(frame, LocalSid, RemoteSid,
                    unchecked((ushort)Interlocked.Increment(ref _civInnerSequence))), token),
            ct);
    }

    private async Task SendTrackedAsync(byte[] packet, CancellationToken ct)
    {
        await _trackedSend.WaitAsync(ct);
        try
        {
            ushort sequence = unchecked((ushort)++_outerSequence);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), sequence);
            await _socket.SendAsync(packet, ct);
        }
        finally { _trackedSend.Release(); }
    }
}
