using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.IcomLan;

public static class IcomLanCivPacketCodec
{
    // Same C1 envelope as the original SkyRoof Direct LAN session.
    public static byte[] Encode(ReadOnlySpan<byte> frame, uint sender, uint receiver,
        ushort innerSequence)
    {
        if (frame.IsEmpty || frame.Length > 65_486)
            throw new ArgumentOutOfRangeException(nameof(frame));
        var packet = new byte[21 + frame.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), sender);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), receiver);
        packet[16] = 0xC1;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(17), (ushort)frame.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(19), innerSequence);
        frame.CopyTo(packet.AsSpan(21));
        return packet;
    }

    public static bool TryRead(byte[] packet, uint local, uint remote,
        out ReadOnlyMemory<byte> payload)
    {
        payload = default;
        if (packet.Length < 22 || packet[16] != 0xC1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet) != packet.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)) != 0 ||
            !IcomLanControlCodec.MatchesEnvelope(packet, local, remote))
            return false;
        int size = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(17));
        if (size != packet.Length - 21) return false;
        payload = packet.AsMemory(21, size);
        return true;
    }
}
