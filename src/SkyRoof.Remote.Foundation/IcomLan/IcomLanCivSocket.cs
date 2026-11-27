using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SkyRoof.Remote.Foundation.IcomLan;

/// <summary>
/// Owns the already-authorized Direct LAN native CI-V UDP endpoint.
/// Full CI-V CAT/Scope routing is added separately; this preserves the
/// original radio's stream-open/close handshake and local port identity.
/// </summary>
public sealed partial class IcomLanCivSocket : IAsyncDisposable
{
    private readonly UdpClient _socket;
    private bool _opened;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _maintenance;
    public Task MaintenanceTask => _maintenance ?? Task.CompletedTask;
    private int _outerSequence;
    private readonly SemaphoreSlim _trackedSend = new(1, 1);
    private readonly IcomLanCivQueries _queries = new();
    public Task QueryFailureTask => _queries.FailureTask;

    private IcomLanCivSocket(UdpClient socket, uint localSid)
    {
        _socket = socket;
        LocalSid = localSid;
    }

    public uint LocalSid { get; }
    public uint RemoteSid { get; private set; }
    public int LocalPort => (_socket.Client.LocalEndPoint as IPEndPoint
        ?? throw new InvalidOperationException("UDP socket is not bound.")).Port;

    public static IcomLanCivSocket Reserve(string address, int controlPort = 50001)
    {
        var ip = IPAddress.Parse(address);
        using var route = new Socket(AddressFamily.InterNetwork,
            SocketType.Dgram, ProtocolType.Udp);
        route.Connect(new IPEndPoint(ip, controlPort));
        var localIp = (route.LocalEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("UDP route has no local endpoint.")).Address;
        var socket = new UdpClient(new IPEndPoint(localIp, 0));
        var local = socket.Client.LocalEndPoint as IPEndPoint
                ?? throw new InvalidOperationException("UDP socket is not bound.");
        socket.Client.ReceiveBufferSize = 1_048_576;
        return new IcomLanCivSocket(socket,
            IcomLanControlCodec.BuildSessionId(local.Address, local.Port));
    }

    public async Task ConnectAsync(string address, int port, CancellationToken ct = default)
    {
        _socket.Connect(IPAddress.Parse(address), port);
        var hello = IcomLanControlCodec.ControlPacket(3, 0, LocalSid, 0);
        await _socket.SendAsync(hello, ct);
        await _socket.SendAsync(hello, ct);
        var greeting = await ReceiveMatchingAsync(
            p => IcomLanControlCodec.TryReadGreeting(p, LocalSid, out _), ct);
        IcomLanControlCodec.TryReadGreeting(greeting, LocalSid, out uint remote);
        RemoteSid = remote;
        var ready = IcomLanControlCodec.ControlPacket(6, 1, LocalSid, RemoteSid);
        await _socket.SendAsync(ready, ct);
        await _socket.SendAsync(ready, ct);
        await ReceiveMatchingAsync(
            p => IcomLanControlCodec.MatchesReady(p, LocalSid, RemoteSid), ct);
        await SendOpenCloseAsync(false, ct);
        _opened = true;
        _maintenance = MaintainAsync(_lifetime.Token);
    }

    private async Task<byte[]> ReceiveMatchingAsync(Func<byte[], bool> matches, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
            {
                var packet = (await _socket.ReceiveAsync(deadline.Token)).Buffer;
                if (matches(packet)) return packet;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Icom native CI-V stream greeting timed out.");
        }
    }

    private Task SendOpenCloseAsync(bool close, CancellationToken ct)
    {
        byte[] packet = new byte[22];
        packet[0] = 22;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), LocalSid);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), RemoteSid);
        packet[16] = 0xC0;
        packet[17] = 1;
        packet[21] = close ? (byte)0 : (byte)5;
        return SendTrackedAsync(packet, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _queries.Stop();
        if (_maintenance is not null)
        {
            try { await _maintenance; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            { Trace.TraceInformation("Icom CI-V maintenance cancelled during shutdown."); }
            catch (IOException error) { Trace.TraceWarning("Icom CI-V shutdown: {0}", error.Message); }
            catch (SocketException error) { Trace.TraceWarning("Icom CI-V shutdown: {0}", error.Message); }
        }
        if (_opened)
        {
            try { await SendOpenCloseAsync(true, CancellationToken.None); }
            catch (SocketException error) { Trace.TraceWarning("Icom CI-V shutdown: {0}", error.Message); }
            var close = IcomLanControlCodec.ControlPacket(5, 0, LocalSid, RemoteSid);
            try
            {
                await _socket.SendAsync(close, CancellationToken.None);
                await _socket.SendAsync(close, CancellationToken.None);
            }
            catch (SocketException error) { Trace.TraceWarning("Icom CI-V shutdown: {0}", error.Message); }
        }
        _socket.Dispose();
        _lifetime.Dispose();
    }
}
