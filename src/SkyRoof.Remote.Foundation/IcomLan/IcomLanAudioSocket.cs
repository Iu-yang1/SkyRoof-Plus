using System.Buffers.Binary;
using System.Diagnostics;
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
    public Task MaintenanceTask => _heartbeat ?? Task.CompletedTask;
    private ushort _txOuterSequence, _txAudioSequence;
    private ushort _pingSequence = 1, _pingInner = 0x8304;

    private IcomLanAudioSocket(UdpClient socket, uint localSid)
    {
        _socket = socket;
        LocalSid = localSid;
    }

    public uint LocalSid { get; }
    public uint RemoteSid { get; private set; }
    public int LocalPort => (_socket.Client.LocalEndPoint as IPEndPoint
        ?? throw new InvalidOperationException("UDP socket is not bound.")).Port;

    // Reserve the real socket *before* the authenticated control grant.
    // Icom's stream request advertises this exact bound UDP port.
    public static IcomLanAudioSocket Reserve(string address, int controlPort = 50001)
    {
        var ip = IPAddress.Parse(address);
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Icom LAN requires an IPv4 address.");
        using var route = new Socket(AddressFamily.InterNetwork,
            SocketType.Dgram, ProtocolType.Udp);
        route.Connect(new IPEndPoint(ip, controlPort));
        var localAddress = (route.LocalEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("UDP route has no local endpoint.")).Address;
        var udp = new UdpClient(new IPEndPoint(localAddress, 0));
        var local = udp.Client.LocalEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("UDP socket is not bound.");
        return new IcomLanAudioSocket(udp,
            IcomLanControlCodec.BuildSessionId(local.Address, local.Port));
    }

    public static async Task<IcomLanAudioSocket> OpenAsync(
        string address, int remotePort, CancellationToken ct = default)
    {
        var session = Reserve(address);
        try
        {
            await session.ConnectAsync(address, remotePort, ct);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    public async Task ConnectAsync(string address, int remotePort, CancellationToken ct = default)
    {
        if (_heartbeat is not null) throw new InvalidOperationException("Audio UDP already negotiated.");
        _socket.Connect(IPAddress.Parse(address), remotePort);
        await HandshakeAsync(ct);
        _heartbeat = HeartbeatAsync(_lifetime.Token);
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
        if (_heartbeat is not null)
        {
            try { await _heartbeat; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            { Trace.TraceInformation("Icom audio maintenance cancelled during shutdown."); }
            catch (ObjectDisposedException error) { Trace.TraceWarning("Icom audio shutdown: {0}", error.Message); }
            catch (SocketException error) { Trace.TraceWarning("Icom audio heartbeat: {0}", error.Message); }
        }
        if (RemoteSid != 0)
        {
            byte[] disconnect = IcomLanControlCodec.ControlPacket(
                5, 0, LocalSid, RemoteSid);
            try
            {
                await _socket.SendAsync(disconnect, CancellationToken.None);
                await _socket.SendAsync(disconnect, CancellationToken.None);
            }
            catch (SocketException error) { Trace.TraceWarning("Icom audio shutdown: {0}", error.Message); }
        }
        _socket.Dispose();
        _sendLock.Dispose();
        _lifetime.Dispose();
    }
}
