using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Foundation.IcomLan;

/// <summary>
/// Audio UDP transport for an existing authenticated media-stream grant.
/// Does not perform login or independently claim a radio's remote slot.
/// </summary>
public sealed partial class IcomLanAudioSocket : IAsyncDisposable
{
    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task? _heartbeat;
    private ushort _pingSequence = 1, _pingInner = 0x8304;

    private IcomLanAudioSocket(UdpClient socket, uint localSid)
    {
        _socket = socket;
        LocalSid = localSid;
    }

    public uint LocalSid { get; }
    public uint RemoteSid { get; private set; }

    public static async Task<IcomLanAudioSocket> OpenAsync(
        string address, int remotePort, CancellationToken ct = default)
    {
        var ip = IPAddress.Parse(address);
        if (ip.AddressFamily != AddressFamily.InterNetwork ||
            remotePort is < 1 or > 65535)
            throw new ArgumentException("Invalid Icom audio UDP endpoint.");
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Connect(ip, remotePort);
        var local = (IPEndPoint)udp.Client.LocalEndPoint!;
        var session = new IcomLanAudioSocket(udp,
            IcomLanControlCodec.BuildSessionId(local.Address, local.Port));
        try
        {
            await session.HandshakeAsync(ct);
            session._heartbeat = session.HeartbeatAsync(session._lifetime.Token);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private async Task HandshakeAsync(CancellationToken ct)
    {
        var hello = IcomLanControlCodec.ControlPacket(3, 0, LocalSid, 0);
        await _socket.SendAsync(hello, ct);
        await _socket.SendAsync(hello, ct);
        var greeting = await ReceiveMatchingAsync(p =>
            IcomLanControlCodec.TryReadGreeting(p, LocalSid, out _), ct);
        IcomLanControlCodec.TryReadGreeting(greeting, LocalSid, out uint remoteSid);
        RemoteSid = remoteSid;
        var ready = IcomLanControlCodec.ControlPacket(6, 1, LocalSid, RemoteSid);
        await _socket.SendAsync(ready, ct);
        await _socket.SendAsync(ready, ct);
        await ReceiveMatchingAsync(p =>
            IcomLanControlCodec.MatchesReady(p, LocalSid, RemoteSid), ct);
    }

    private async Task<byte[]> ReceiveMatchingAsync(Func<byte[], bool> matches, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
            {
                byte[] packet = (await _socket.ReceiveAsync(deadline.Token)).Buffer;
                if (matches(packet)) return packet;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Icom audio UDP handshake did not complete.");
        }
    }

    public async Task<byte[]> ReadAudioFrameAsync(
        IcomLanAudioCodec codec, CancellationToken ct = default)
    {
        while (true)
        {
            byte[] packet = (await _socket.ReceiveAsync(ct)).Buffer;
            if (packet.Length == 16 &&
                BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)) == 5 &&
                IcomLanControlCodec.MatchesEnvelope(packet, LocalSid, RemoteSid))
                throw new IOException("Authenticated Icom audio UDP stream disconnected.");
            if (packet.Length == 21 && await ReplyToRemotePingAsync(packet, ct))
                continue;
            if (IcomLanAudioPacketCodec.TryDecode(packet, RemoteSid, LocalSid,
                codec, out _))
                return packet;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _socket.Dispose();
        if (_heartbeat is not null)
        {
            try { await _heartbeat; }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        _sendLock.Dispose();
        _lifetime.Dispose();
    }
}
