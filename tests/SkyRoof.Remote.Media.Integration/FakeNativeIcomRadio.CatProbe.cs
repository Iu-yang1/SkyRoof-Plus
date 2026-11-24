using System.Buffers.Binary;
using System.Net;
using SkyRoof.Remote.Foundation.IcomLan;

internal sealed partial class FakeNativeIcomRadio
{
    private async Task ServeCatProbeAsync(IPEndPoint peer, uint local, CancellationToken ct)
    {
        await RespondToQueryAsync(peer, local, [0x03],
            [0, 0, 0x65, 0x35, 4], 1, ct);
        await RespondToQueryAsync(peer, local, [0x1C, 0], [0], 2, ct);
        Console.WriteLine("PASS: independent Device sent native CI-V frequency/PTT queries.");
    }

    private async Task RespondToQueryAsync(IPEndPoint peer, uint local,
        byte[] command, byte[] data, ushort sequence, CancellationToken ct)
    {
        byte[] expected = new IcomCivQuery(0xA2, 0xE0, command, 0).BuildRequest();
        while (true)
        {
            byte[] packet = (await _civ.ReceiveAsync(ct)).Buffer;
            if (!IcomLanCivPacketCodec.TryRead(packet, CivSid, local, out var payload)) continue;
            if (!payload.Span.SequenceEqual(expected))
                throw new InvalidDataException("Unexpected native CI-V query.");
            break;
        }
        byte[] reply = [0xFE, 0xFE, 0xE0, 0xA2, .. command, .. data, 0xFD];
        byte[] datagram = IcomLanCivPacketCodec.Encode(reply, CivSid, local, sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(6), sequence);
        await _civ.SendAsync(datagram, peer, ct);
    }
}
