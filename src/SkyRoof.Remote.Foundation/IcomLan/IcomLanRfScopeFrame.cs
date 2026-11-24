using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.IcomLan;

// Raw scope magnitudes 0–160, not calibrated dBm. Geometry comes from CI-V.
public sealed record IcomLanRfScopeFrame(byte Band, byte Mode, bool OutOfRange,
    long LowerHz, long UpperHz, byte[] Samples)
{
    public const int Points = 475;
    public const int HeaderSize = 24;
    public const uint Magic = 0x31465253; // SRF1

    public byte[] Encode()
    {
        if (Samples.Length != Points) throw new InvalidDataException("Invalid RF scope size.");
        byte[] data = new byte[HeaderSize + Points];
        BinaryPrimitives.WriteUInt32LittleEndian(data, Magic);
        data[4] = Band;
        data[5] = Mode;
        data[6] = OutOfRange ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), LowerHz);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), UpperHz);
        Samples.CopyTo(data, HeaderSize);
        return data;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out IcomLanRfScopeFrame? frame)
    {
        frame = null;
        if (data.Length != HeaderSize + Points ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic ||
            data[4] > 1 || data[5] > 3 || data[6] > 1)
            return false;
        long lower = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        long upper = BinaryPrimitives.ReadInt64LittleEndian(data[16..]);
        if (lower >= upper || data[HeaderSize..].IndexOfAnyInRange((byte)161, byte.MaxValue) >= 0)
            return false;
        frame = new(data[4], data[5], data[6] == 1, lower, upper, data[HeaderSize..].ToArray());
        return true;
    }
}
