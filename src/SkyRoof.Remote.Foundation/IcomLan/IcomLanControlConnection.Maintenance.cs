using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanControlConnection
{
    private DateTime _lastIdle, _lastPing, _lastAuth;
    private DateTime _lastRenewalRequest;
    private ushort? _pendingRenewal;
    private byte[] _expectedRenewalId = [];

    private async Task MaintainAsync(CancellationToken ct)
    {
        _lastAuth = DateTime.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var read = _control.ReceiveAsync(ct).AsTask();
        var tick = timer.WaitForNextTickAsync(ct).AsTask();
        while (!ct.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(read, tick);
            if (completed == read)
            {
                await ProcessMaintenancePacketAsync((await read).Buffer, ct);
                read = _control.ReceiveAsync(ct).AsTask();
            }
            else
            {
                if (!await tick) break;
                await MaintainTimersAsync(ct);
                tick = timer.WaitForNextTickAsync(ct).AsTask();
            }
        }
    }

    private async Task ProcessMaintenancePacketAsync(byte[] packet, CancellationToken ct)
    {
        if (packet.Length < 16 ||
            !IcomLanControlCodec.MatchesEnvelope(packet, LocalSid, RemoteSid))
            return;
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));
        if (type == 5 && packet.Length == 16)
            throw new IOException("Icom control UDP session was explicitly disconnected.");
        if (type == 7 && packet.Length == 21 && packet[16] == 0 && packet[20] == 6)
        {
            byte[] reply = packet.ToArray();
            reply[16] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8, 4), LocalSid);
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12, 4), RemoteSid);
            await SendAsync(reply, tracked: false, ct);
        }
        else if (_pendingRenewal is ushort seq &&
            IcomLanControlCodec.MatchesAuth(
                packet, LocalSid, RemoteSid, seq, 5, _expectedRenewalId))
        {
            _authId = packet.AsSpan(0x1A, 6).ToArray();
            _pendingRenewal = null;
            _lastAuth = DateTime.UtcNow;
        }
    }

    private async Task MaintainTimersAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastIdle >= TimeSpan.FromSeconds(1))
        {
            await SendAsync(IcomLanControlCodec.ControlPacket(0, 0, LocalSid, RemoteSid),
                tracked: true, ct);
            _lastIdle = now;
        }
        if (now - _lastPing >= TimeSpan.FromSeconds(3))
        {
            byte[] ping = new byte[21];
            BinaryPrimitives.WriteUInt32LittleEndian(ping, 21);
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(4, 2), 7);
            ping[17] = RandomNumberGenerator.GetBytes(1)[0];
            BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(18, 2),
                NextInnerSequence());
            ping[20] = 6;
            BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(8, 4), LocalSid);
            BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(12, 4), RemoteSid);
            await SendAsync(ping, tracked: true, ct);
            _lastPing = now;
        }
        if (_pendingRenewal.HasValue && now - _lastAuth >= TimeSpan.FromSeconds(50))
            throw new TimeoutException("Icom LAN authentication token renewal was not acknowledged.");
        if (_pendingRenewal is null && now - _lastAuth >= TimeSpan.FromSeconds(20) ||
            _pendingRenewal.HasValue && now - _lastRenewalRequest >= TimeSpan.FromMilliseconds(2500))
        {
            _expectedRenewalId = _authId.ToArray();
            _pendingRenewal = NextInnerSequence();
            await SendAsync(IcomLanControlCodec.BuildAuth(LocalSid, RemoteSid,
                _pendingRenewal.Value, 5, _expectedRenewalId), tracked: true, ct);
            _lastRenewalRequest = now;
        }
    }
}
