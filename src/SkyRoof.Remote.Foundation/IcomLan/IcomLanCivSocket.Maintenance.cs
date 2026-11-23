using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanCivSocket
{
    private ushort _innerPingSequence = 0x8304;
    private DateTime _lastIdle, _lastPing;

    private async Task MaintainAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var read = _socket.ReceiveAsync(ct).AsTask();
        var tick = timer.WaitForNextTickAsync(ct).AsTask();
        while (!ct.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(read, tick);
            if (completed == read)
            {
                await HandleIncomingAsync((await read).Buffer, ct);
                read = _socket.ReceiveAsync(ct).AsTask();
            }
            else
            {
                if (!await tick) break;
                await MaintainTimersAsync(ct);
                tick = timer.WaitForNextTickAsync(ct).AsTask();
            }
        }
    }

    private async Task HandleIncomingAsync(byte[] packet, CancellationToken ct)
    {
        if (packet.Length < 16 ||
            !IcomLanControlCodec.MatchesEnvelope(packet, LocalSid, RemoteSid))
            return;
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));
        if (packet.Length == 16 && type == 5)
            throw new IOException("Authenticated Icom CI-V UDP stream disconnected.");
        if (packet.Length == 21 && type == 7 && packet[16] == 0 && packet[20] == 6)
        {
            var reply = packet.ToArray();
            reply[16] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8, 4), LocalSid);
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12, 4), RemoteSid);
            await _socket.SendAsync(reply, ct);
        }
    }

    private async Task MaintainTimersAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastIdle >= TimeSpan.FromSeconds(1))
        {
            var idle = IcomLanControlCodec.ControlPacket(
                0, unchecked(_outerSequence++), LocalSid, RemoteSid);
            await _socket.SendAsync(idle, ct);
            _lastIdle = now;
        }
        if (now - _lastPing >= TimeSpan.FromSeconds(3))
        {
            byte[] ping = new byte[21];
            BinaryPrimitives.WriteUInt32LittleEndian(ping, 21);
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(4, 2), 7);
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(6, 2),
                unchecked(_outerSequence++));
            BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(8, 4), LocalSid);
            BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(12, 4), RemoteSid);
            ping[17] = RandomNumberGenerator.GetBytes(1)[0];
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(18, 2),
                unchecked(_innerPingSequence++));
            ping[20] = 6;
            await _socket.SendAsync(ping, ct);
            _lastPing = now;
        }
    }
}
