using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

internal sealed class FakeIcomLanControlRadio : IDisposable
{
    private const uint RadioSid = 0xAABBCCDD;
    private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly byte[] _token = [0x34, 0x12, 0x78, 0x56, 0x34, 0x12];
    private readonly byte[] _renewed = [0x34, 0x12, 0x90, 0x89, 0x78, 0x67];
    public int Port => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;

    public async Task RunAsync(CancellationToken ct)
    {
        var hello = await ReceiveAsync(ct);
        uint localSid = BinaryPrimitives.ReadUInt32BigEndian(hello.Buffer.AsSpan(8, 4));
        Check(BinaryPrimitives.ReadUInt16LittleEndian(hello.Buffer.AsSpan(4, 2)) == 3);
        _ = await ReceiveAsync(ct); // Duplicate greeting.
        var incorrectHello = SkyRoof.Remote.Foundation.IcomLan.IcomLanControlCodec
            .ControlPacket(4, 0, RadioSid, localSid + 1);
        await _socket.SendAsync(incorrectHello, hello.RemoteEndPoint, ct);
        await _socket.SendAsync(
            SkyRoof.Remote.Foundation.IcomLan.IcomLanControlCodec
                .ControlPacket(4, 0, RadioSid, localSid), hello.RemoteEndPoint, ct);
        var ready = await ReceiveAsync(ct);
        Check(BinaryPrimitives.ReadUInt16LittleEndian(ready.Buffer.AsSpan(4, 2)) == 6);
        _ = await ReceiveAsync(ct);
        await _socket.SendAsync(
            SkyRoof.Remote.Foundation.IcomLan.IcomLanControlCodec
                .ControlPacket(6, 1, RadioSid, localSid), hello.RemoteEndPoint, ct);

        var login = await ReceiveAsync(ct);
        Check(login.Buffer.Length == 0x80 && login.Buffer[0x15] == 0);
        ushort loginSeq = BinaryPrimitives.ReadUInt16BigEndian(login.Buffer.AsSpan(0x16, 2));
        ushort tokenRequest = BinaryPrimitives.ReadUInt16LittleEndian(login.Buffer.AsSpan(0x1A, 2));
        var badToken = Reply(0x60, localSid, loginSeq, 2, 0, _token);
        BinaryPrimitives.WriteUInt16LittleEndian(badToken.AsSpan(0x1A, 2),
            unchecked((ushort)(tokenRequest + 1)));
        await _socket.SendAsync(badToken, hello.RemoteEndPoint, ct);
        var validLogin = Reply(0x60, localSid, loginSeq, 2, 0, _token);
        BinaryPrimitives.WriteUInt16LittleEndian(validLogin.AsSpan(0x1A, 2), tokenRequest);
        validLogin.AsSpan(0x1A, 2).CopyTo(_token.AsSpan(0, 2));
        validLogin.AsSpan(0x1A, 2).CopyTo(_renewed.AsSpan(0, 2));
        await _socket.SendAsync(validLogin, hello.RemoteEndPoint, ct);

        var capRequest = await ReceiveAsync(ct);
        var tokenReq = await ReceiveAsync(ct);
        Check(capRequest.Buffer.Length == 0x40 && capRequest.Buffer[0x15] == 2 &&
            tokenReq.Buffer.Length == 0x40 && tokenReq.Buffer[0x15] == 5);
        ushort capSeq = BinaryPrimitives.ReadUInt16BigEndian(capRequest.Buffer.AsSpan(0x16, 2));
        ushort authSeq = BinaryPrimitives.ReadUInt16BigEndian(tokenReq.Buffer.AsSpan(0x16, 2));
        var authReply = Reply(0x40, localSid, authSeq, 2, 5, _renewed);
        await _socket.SendAsync(authReply, hello.RemoteEndPoint, ct);
        var caps = Reply(0x42 + 0x66, localSid, capSeq, 2, 2, _token);
        System.Text.Encoding.ASCII.GetBytes("IC-9700").CopyTo(caps, 0x42 + 0x10);
        System.Text.Encoding.ASCII.GetBytes("Remote Audio").CopyTo(caps, 0x42 + 0x30);
        caps[0x42 + 0x52] = 0xA2;
        BinaryPrimitives.WriteUInt16BigEndian(caps.AsSpan(0x42 + 7, 2), 0x8010);
        BinaryPrimitives.WriteUInt16BigEndian(caps.AsSpan(0x42 + 0x53, 2), 0x018B);
        await _socket.SendAsync(caps, hello.RemoteEndPoint, ct);

        var stream = await ReceiveAsync(ct);
        Check(stream.Buffer.Length == 0x90 && stream.Buffer[0x15] == 3 &&
            stream.Buffer.AsSpan(0x1A, 6).SequenceEqual(_renewed) &&
            BinaryPrimitives.ReadUInt32BigEndian(stream.Buffer.AsSpan(0x74, 4)) == 48000 &&
            BinaryPrimitives.ReadUInt32BigEndian(stream.Buffer.AsSpan(0x7C, 4)) == 41002 &&
            BinaryPrimitives.ReadUInt32BigEndian(stream.Buffer.AsSpan(0x80, 4)) == 41003);
        ushort streamSeq = BinaryPrimitives.ReadUInt16BigEndian(stream.Buffer.AsSpan(0x16, 2));
        var wrong = Reply(0x50, localSid + 1, streamSeq, 2, 3, _renewed);
        await _socket.SendAsync(wrong, hello.RemoteEndPoint, ct);
        var status = Reply(0x50, localSid, streamSeq, 2, 3, _renewed);
        BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(0x42, 2), 50002);
        BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(0x46, 2), 50003);
        await _socket.SendAsync(status, hello.RemoteEndPoint, ct);

        // Logout must use the currently renewed token. Ignore background
        // Idle/Ping traffic while waiting for the correlated auth mutation.
        UdpReceiveResult logout;
        do
        {
            logout = await ReceiveAsync(ct);
        } while (logout.Buffer.Length != 0x40 || logout.Buffer[0x15] != 1);
        Check(logout.Buffer.AsSpan(0x1A, 6).SequenceEqual(_renewed));
        ushort logoutSequence = BinaryPrimitives.ReadUInt16BigEndian(
            logout.Buffer.AsSpan(0x16, 2));
        await _socket.SendAsync(
            Reply(0x40, localSid, logoutSequence, 2, 1, _renewed),
            hello.RemoteEndPoint, ct);
        UdpReceiveResult disconnected;
        do
        {
            disconnected = await ReceiveAsync(ct);
        } while (disconnected.Buffer.Length != 16 ||
                 BinaryPrimitives.ReadUInt16LittleEndian(
                     disconnected.Buffer.AsSpan(4, 2)) != 5);
        Check(BinaryPrimitives.ReadUInt32BigEndian(
            disconnected.Buffer.AsSpan(8, 4)) == localSid);
    }

    private Task<UdpReceiveResult> ReceiveAsync(CancellationToken ct) =>
        _socket.ReceiveAsync(ct).AsTask();

    private static byte[] Reply(int length, uint localSid, ushort sequence,
        byte type, byte kind, byte[] authId)
    {
        byte[] bytes = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), RadioSid);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12, 4), localSid);
        bytes[0x14] = type;
        bytes[0x15] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0x16, 2), sequence);
        authId.CopyTo(bytes, 0x1A);
        return bytes;
    }

    private static void Check(bool valid)
    {
        if (!valid) throw new InvalidOperationException("Fake Direct LAN radio rejected session step.");
    }

    public void Dispose() => _socket.Dispose();
}
