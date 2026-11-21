using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.Signals;

// SAP/1: 52-byte little-endian fixed header. UTC timestamp uses Unix nanoseconds.
// For accuracy and continuity, FirstSampleIndex is authoritative over network arrival time.
public readonly record struct AudioFrameHeader(
    byte Codec,
    ushort Flags,
    uint StreamId,
    uint StreamEpoch,
    ulong Sequence,
    ulong FirstSampleIndex,
    long UtcNanoseconds,
    uint SampleRate,
    ushort SamplesPerChannel,
    byte Channels,
    uint PayloadLength)
{
    public const uint Magic = 0x31504153; // "SAP1" in little endian
    public const byte Version = 1;
    public const int Size = 52;

    public void Write(Span<byte> destination)
    {
        if (destination.Length < Size) throw new ArgumentException("Header destination is too short.", nameof(destination));
        if (Codec is < 1 or > 3 || Channels is 0 || SampleRate is 0)
            throw new ArgumentException("Unsupported audio frame metadata.");
        BinaryPrimitives.WriteUInt32LittleEndian(destination[0..4], Magic);
        destination[4] = Version;
        destination[5] = Codec;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..8], Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..12], StreamId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..16], StreamEpoch);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..24], Sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..32], FirstSampleIndex);
        BinaryPrimitives.WriteInt64LittleEndian(destination[32..40], UtcNanoseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[40..44], SampleRate);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[44..46], SamplesPerChannel);
        destination[46] = Channels;
        destination[47] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[48..52], PayloadLength);
    }

    public static bool TryRead(ReadOnlySpan<byte> packet, out AudioFrameHeader header)
    {
        header = default;
        if (packet.Length < Size || BinaryPrimitives.ReadUInt32LittleEndian(packet[0..4]) != Magic
            || packet[4] != Version || packet[5] is < 1 or > 3 || packet[46] == 0
            || BinaryPrimitives.ReadUInt32LittleEndian(packet[40..44]) == 0)
            return false;
        var bytes = BinaryPrimitives.ReadUInt32LittleEndian(packet[48..52]);
        if (bytes > (uint)(packet.Length - Size)) return false;
        header = new AudioFrameHeader(
            packet[5],
            BinaryPrimitives.ReadUInt16LittleEndian(packet[6..8]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[8..12]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[12..16]),
            BinaryPrimitives.ReadUInt64LittleEndian(packet[16..24]),
            BinaryPrimitives.ReadUInt64LittleEndian(packet[24..32]),
            BinaryPrimitives.ReadInt64LittleEndian(packet[32..40]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[40..44]),
            BinaryPrimitives.ReadUInt16LittleEndian(packet[44..46]),
            packet[46], bytes);
        return true;
    }
}
