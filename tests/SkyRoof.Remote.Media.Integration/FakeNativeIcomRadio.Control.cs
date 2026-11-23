using System.Buffers.Binary;
using SkyRoof.Remote.Foundation.IcomLan;

internal sealed partial class FakeNativeIcomRadio
{
    private async Task ServeControlAsync(CancellationToken ct)
    {
        var greet = await _control.ReceiveAsync(ct);
        uint local = LocalSid(greet.Buffer);
        var peer = greet.RemoteEndPoint;
        _ = await _control.ReceiveAsync(ct);
        await _control.SendAsync(
            IcomLanControlCodec.ControlPacket(4, 0, ControlSid, local), peer, ct);
        _ = await _control.ReceiveAsync(ct);
        _ = await _control.ReceiveAsync(ct);
        await _control.SendAsync(
            IcomLanControlCodec.ControlPacket(6, 1, ControlSid, local), peer, ct);

        var login = await _control.ReceiveAsync(ct);
        var loginResponse = Reply(0x60, ControlSid, local,
            Sequence(login.Buffer), 2, 0, _auth);
        login.Buffer.AsSpan(0x1A, 2).CopyTo(loginResponse.AsSpan(0x1A, 2));
        loginResponse.AsSpan(0x1A, 2).CopyTo(_auth.AsSpan(0, 2));
        await _control.SendAsync(loginResponse, peer, ct);

        var capabilitiesRequest = await _control.ReceiveAsync(ct);
        var tokenRequest = await _control.ReceiveAsync(ct);
        var tokenReply = Reply(0x40, ControlSid, local,
            Sequence(tokenRequest.Buffer), 2, 5, _auth);
        await _control.SendAsync(tokenReply, peer, ct);
        var capabilities = Reply(0x42 + 0x66, ControlSid, local,
            Sequence(capabilitiesRequest.Buffer), 2, 2, _auth);
        System.Text.Encoding.ASCII.GetBytes("IC-9700").CopyTo(capabilities, 0x42 + 0x10);
        capabilities[0x42 + 0x52] = 0xA2;
        BinaryPrimitives.WriteUInt16BigEndian(capabilities.AsSpan(0x42 + 7, 2), 0x8010);
        BinaryPrimitives.WriteUInt16BigEndian(capabilities.AsSpan(0x42 + 0x53, 2), 0x0008);
        await _control.SendAsync(capabilities, peer, ct);

        var stream = await _control.ReceiveAsync(ct);
        var data = stream.Buffer;
        if (data.Length != 0x90 || data[0x70] != 1 ||
            data[0x72] != 4 ||
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x7C, 4)) == 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x80, 4)) == 0)
            throw new InvalidDataException("Device grant omitted reserved native media ports.");
        var status = Reply(0x50, ControlSid, local,
            Sequence(data), 2, 3, _auth);
        BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(0x42, 2), (ushort)CivPort);
        BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(0x46, 2), (ushort)AudioPort);
        await _control.SendAsync(status, peer, ct);
    }
}
