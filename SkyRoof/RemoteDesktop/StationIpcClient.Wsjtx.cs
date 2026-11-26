using System.Buffers.Binary;
using System.Text;

namespace SkyRoof.RemoteDesktop;

// WSJT-X QDataStream schema 2/3, message type 12 (Logged ADIF).
// Type 5 and all command packets are ignored to avoid duplicate QSO events.
internal sealed partial class StationIpcClient
{
    internal static string? ReadLoggedAdif(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(packet) != 0xadbccbda)
            return null;
        uint schema = BinaryPrimitives.ReadUInt32BigEndian(packet[4..]);
        if (schema is not (2 or 3) || BinaryPrimitives.ReadUInt32BigEndian(packet[8..]) != 12)
            return null;
        var payload = packet[12..];
        _ = ReadQtBytes(ref payload); // Sender instance ID, not station callsign.
        var adif = ReadQtBytes(ref payload);
        if (!payload.IsEmpty) throw new InvalidDataException("Unexpected Logged ADIF trailing bytes.");
        return new UTF8Encoding(false, true).GetString(adif);
    }

    private static ReadOnlySpan<byte> ReadQtBytes(ref ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4) throw new InvalidDataException("Truncated Qt byte-array length.");
        uint length = BinaryPrimitives.ReadUInt32BigEndian(payload);
        payload = payload[4..];
        if (length == uint.MaxValue) return [];
        if (length > payload.Length) throw new InvalidDataException("Truncated Qt byte-array value.");
        var value = payload[..(int)length];
        payload = payload[(int)length..];
        return value;
    }
}
