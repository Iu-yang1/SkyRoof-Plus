using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SkyRoof.Remote.Foundation.IcomLan;

internal sealed class IcomLanCivWriteRadio(UdpClient radio)
{
    private const uint Sid = 0xAABBCCDD;
    private uint _local;
    private IPEndPoint? _peer;
    private ushort _sequence;

    public async Task RunAsync(CancellationToken ct)
    {
        var hello = await radio.ReceiveAsync(ct);
        _peer = hello.RemoteEndPoint;
        _local = BinaryPrimitives.ReadUInt32BigEndian(hello.Buffer.AsSpan(8));
        _ = await radio.ReceiveAsync(ct);
        await SendAsync(IcomLanControlCodec.ControlPacket(4, 0, Sid, _local), ct);
        _ = await radio.ReceiveAsync(ct);
        _ = await radio.ReceiveAsync(ct);
        await SendAsync(IcomLanControlCodec.ControlPacket(6, 1, Sid, _local), ct);
        _ = await radio.ReceiveAsync(ct);
        await ExchangeAsync([7, 0xD0], [0xFB], ct);
        await ExchangeAsync([5, 0, 0, 0x65, 0x35, 4], [0xFB], ct);
        await ExchangeAsync([3], [3, 0, 0, 0x65, 0x35, 4], ct);
        await ExchangeAsync([6, 1, 2], [0xFB], ct);
        await ExchangeAsync([4], [4, 1, 2], ct);
        await ExchangeAsync([0x1C, 0, 1], [0xFB], ct);
        await ExchangeAsync([0x1C, 0], [0x1C, 0, 1], ct);
        await ExchangeAsync([0x1C, 0, 0], [0xFB], ct);
        await ExchangeAsync([0x1C, 0], [0x1C, 0, 0], ct);
        await ExchangeAsync([0x15, 2], [0x15, 2, 1, 0x20], ct);
        await ExchangeAsync([7, 0xD1], [0xFA], ct);
    }

    private async Task ExchangeAsync(byte[] command, byte[] response, CancellationToken ct)
    {
        byte[] expected = new IcomCivQuery(0xA2, 0xE0, command, 0).BuildRequest();
        while (true)
        {
            byte[] packet = (await radio.ReceiveAsync(ct)).Buffer;
            if (!IcomLanCivPacketCodec.TryRead(packet, Sid, _local, out var data)) continue;
            if (!data.Span.SequenceEqual(expected))
                throw new InvalidOperationException("Unexpected native write/read request.");
            break;
        }
        byte[] frame = [0xFE, 0xFE, 0xE0, 0xA2, .. response, 0xFD];
        ushort sequence = ++_sequence;
        byte[] reply = IcomLanCivPacketCodec.Encode(frame, Sid, _local, sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(6), sequence);
        await SendAsync(reply, ct);
    }

    private async Task SendAsync(byte[] packet, CancellationToken ct) =>
        _ = await radio.SendAsync(packet,
            _peer ?? throw new InvalidOperationException("Fake radio peer not established."), ct);
}
