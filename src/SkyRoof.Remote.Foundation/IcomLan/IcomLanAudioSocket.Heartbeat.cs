using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanAudioSocket
{
    private async Task HeartbeatAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(ct))
        {
            byte[] ping = new byte[21];
            BinaryPrimitives.WriteUInt32LittleEndian(ping, 21);
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(4, 2), 7);
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(6, 2),
                unchecked(_pingSequence++));
            BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(8, 4), LocalSid);
            BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(12, 4), RemoteSid);
            ping[17] = RandomNumberGenerator.GetBytes(1)[0];
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(18, 2),
                unchecked(_pingInner++));
            ping[20] = 6;
            await SendAsync(ping, ct);
        }
    }

    private async Task<bool> ReplyToRemotePingAsync(byte[] packet, CancellationToken ct)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != 21 ||
            BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)) != 7 ||
            !IcomLanControlCodec.MatchesEnvelope(packet, LocalSid, RemoteSid) ||
            packet[20] != 6)
            return false;
        if (packet[16] == 0)
        {
            byte[] reply = packet.ToArray();
            reply[16] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8, 4), LocalSid);
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12, 4), RemoteSid);
            await SendAsync(reply, ct);
        }
        return true;
    }

    private async Task SendAsync(byte[] packet, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try { await _socket.SendAsync(packet, ct); }
        finally { _sendLock.Release(); }
    }
}
