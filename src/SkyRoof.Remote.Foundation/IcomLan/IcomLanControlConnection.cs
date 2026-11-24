using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed record IcomLanConnectionOptions(
    string Address, int ControlPort, string Username, string Password,
    string ClientName, int LocalCivPort, int LocalAudioPort,
    uint PreferredRxRate = 48000, byte RxCodec = 4)
{
    public uint PreferredTxRate { get; init; }
    public byte TxCodec { get; init; } = 4;
    public uint TxBufferMs { get; init; } = 60;
}

/// <summary>
/// Owns the Icom control UDP login, correlated token and media grant.
/// A separate Device transport owns the negotiated CI-V/audio sockets.
/// </summary>
public sealed partial class IcomLanControlConnection : IAsyncDisposable
{
    private readonly UdpClient _control;
    private ushort _innerSequence;
    private ushort _outerSequence = 1;
    private byte[] _authId = new byte[6];
    private bool _authenticated;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _maintenance;
    public Task MaintenanceTask => _maintenance ?? Task.CompletedTask;

    private IcomLanControlConnection(UdpClient control, uint localSid)
    {
        _control = control;
        LocalSid = localSid;
    }

    public uint LocalSid { get; }
    public uint RemoteSid { get; private set; }
    public IcomLanRadioCapabilities? Capabilities { get; private set; }
    public uint NegotiatedRxRate { get; private set; }
    public uint NegotiatedTxRate { get; private set; }
    public byte NegotiatedTxCodec { get; private set; }
    public int RemoteCivPort { get; private set; } = 50002;
    public int RemoteAudioPort { get; private set; } = 50003;

    public static async Task<IcomLanControlConnection> OpenAsync(
        IcomLanConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var remote = IPAddress.Parse(options.Address);
        if (remote.AddressFamily != AddressFamily.InterNetwork ||
            options.ControlPort is < 1 or > 65535)
            throw new ArgumentException("Direct Icom LAN requires an IPv4 control endpoint.");
        var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.Connect(remote, options.ControlPort);
            var local = socket.Client.LocalEndPoint as IPEndPoint
                ?? throw new InvalidOperationException("UDP socket is not bound.");
            uint localSid = IcomLanControlCodec.BuildSessionId(local.Address, local.Port);
            var session = new IcomLanControlConnection(socket, localSid);
            try
            {
                await session.NegotiateAsync(options, cancellationToken);
                session._maintenance = session.MaintainAsync(session._lifetime.Token);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private ushort NextInnerSequence() => unchecked(_innerSequence++);
    private async Task SendAsync(byte[] frame, bool tracked, CancellationToken ct)
    {
        if (tracked)
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2),
                unchecked(_outerSequence++));
        await _control.SendAsync(frame, ct);
    }

    private async Task<byte[]> WaitForAsync(Func<byte[], bool> matches,
        TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var packet = (await _control.ReceiveAsync(deadline.Token)).Buffer;
                if (matches(packet)) return packet;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Icom LAN did not return a correlated control reply.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_maintenance is not null)
        {
            try { await _maintenance; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            { Trace.TraceInformation("Icom control maintenance cancelled during shutdown."); }
            catch (IOException error) { Trace.TraceWarning("Icom control shutdown: {0}", error.Message); }
            catch (SocketException error) { Trace.TraceWarning("Icom control maintenance: {0}", error.Message); }
            catch (TimeoutException error) { Trace.TraceWarning("Icom control shutdown: {0}", error.Message); }
        }
        if (_authenticated)
        {
            _authenticated = false;
            ushort sequence = NextInnerSequence();
            byte[] authId = _authId.ToArray();
            var logout = IcomLanControlCodec.BuildAuth(
                LocalSid, RemoteSid, sequence, 1, authId);
            try
            {
                await SendAsync(logout, tracked: true, CancellationToken.None);
                // Logout remains a correlated token mutation, not a blind send.
                await WaitForAsync(packet => IcomLanControlCodec.MatchesAuth(
                    packet, LocalSid, RemoteSid, sequence, 1, authId),
                    TimeSpan.FromMilliseconds(400), CancellationToken.None);
            }
            catch (TimeoutException error) { Trace.TraceWarning("Icom control shutdown: {0}", error.Message); }
            catch (SocketException error) { Trace.TraceWarning("Icom control shutdown: {0}", error.Message); }
        }
        try
        {
            var disconnect = IcomLanControlCodec.ControlPacket(5, 0, LocalSid, RemoteSid);
            await SendAsync(disconnect, tracked: false, CancellationToken.None);
            await SendAsync(disconnect, tracked: false, CancellationToken.None);
        }
        catch (SocketException error) { Trace.TraceWarning("Icom control shutdown: {0}", error.Message); }
        _control.Dispose();
        _lifetime.Dispose();
    }
}
