using System.Buffers.Binary;
using System.Net;
using SkyRoof.Remote.Foundation.IcomLan;

internal sealed partial class FakeNativeIcomRadio
{
    private async Task ServeCivAsync(CancellationToken ct)
    {
        var hello = await _civ.ReceiveAsync(ct);
        uint local = LocalSid(hello.Buffer);
        var peer = hello.RemoteEndPoint;
        _ = await _civ.ReceiveAsync(ct);
        await _civ.SendAsync(
            IcomLanControlCodec.ControlPacket(4, 0, CivSid, local), peer, ct);
        _ = await _civ.ReceiveAsync(ct);
        _ = await _civ.ReceiveAsync(ct);
        await _civ.SendAsync(
            IcomLanControlCodec.ControlPacket(6, 1, CivSid, local), peer, ct);
        var open = await _civ.ReceiveAsync(ct);
        if (open.Buffer.Length != 22 || open.Buffer[16] != 0xC0 ||
            open.Buffer[21] != 5)
            throw new InvalidDataException("Native CI-V stream-open was not transmitted.");
        await ServeCatProbeAsync(peer, local, ct);
        await ServeNativeCatAsync(peer, local, ct);
    }

    private async Task ServeAudioAsync(CancellationToken ct)
    {
        var hello = await _audio.ReceiveAsync(ct);
        uint local = LocalSid(hello.Buffer);
        var peer = hello.RemoteEndPoint;
        _ = await _audio.ReceiveAsync(ct);
        await _audio.SendAsync(
            IcomLanControlCodec.ControlPacket(4, 0, AudioSid, local), peer, ct);
        _ = await _audio.ReceiveAsync(ct);
        _ = await _audio.ReceiveAsync(ct);
        await _audio.SendAsync(
            IcomLanControlCodec.ControlPacket(6, 1, AudioSid, local), peer, ct);

        await _audio.SendAsync(AudioFrame(local + 1, 49, 0x7F), peer, ct);
        foreach (var (seq, seed) in new (ushort, byte)[]
        {
            (50, 0x11), (52, 0x33), (51, 0x22), (54, 0x44)
        })
        {
            await _audio.SendAsync(AudioFrame(local, seq, seed), peer, ct);
            await Task.Delay(30, ct);
        }
        await ObserveTxAsync(local, ct);
    }

    private static byte[] AudioFrame(uint receiverSid, ushort sequence, byte seed)
    {
        var packet = new byte[24 + 960];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), AudioSid);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), receiverSid);
        packet[16] = 0x80;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), sequence);
        Array.Fill(packet, seed, 24, 960);
        return packet;
    }
}
