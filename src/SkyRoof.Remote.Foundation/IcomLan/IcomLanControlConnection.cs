using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed record IcomLanConnectionOptions(
    string Address, int ControlPort, string Username, string Password,
    string ClientName, int LocalCivPort, int LocalAudioPort,
    uint PreferredRxRate = 48000, byte RxCodec = 4);

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

    private IcomLanControlConnection(UdpClient control, uint localSid)
    {
        _control = control;
        LocalSid = localSid;
    }

    public uint LocalSid { get; }
    public uint RemoteSid { get; private set; }
    public IcomLanRadioCapabilities? Capabilities { get; private set; }
    public uint NegotiatedRxRate { get; private set; }
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
            var local = (IPEndPoint)socket.Client.LocalEndPoint!;
            uint localSid = IcomLanControlCodec.BuildSessionId(local.Address, local.Port);
            var session = new IcomLanControlConnection(socket, localSid);
            try
            {
                await session.NegotiateAsync(options, cancellationToken);
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
        if (_authenticated)
        {
            _authenticated = false;
            var logout = IcomLanControlCodec.BuildAuth(
                LocalSid, RemoteSid, NextInnerSequence(), 1, _authId);
            try { await SendAsync(logout, tracked: true, CancellationToken.None); }
            catch (SocketException) { }
        }
        try
        {
            var disconnect = IcomLanControlCodec.ControlPacket(5, 0, LocalSid, RemoteSid);
            await SendAsync(disconnect, tracked: false, CancellationToken.None);
            await SendAsync(disconnect, tracked: false, CancellationToken.None);
        }
        catch (SocketException) { }
        _control.Dispose();
    }
}
