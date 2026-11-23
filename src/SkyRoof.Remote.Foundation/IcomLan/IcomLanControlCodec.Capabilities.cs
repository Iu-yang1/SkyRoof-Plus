using System.Buffers.Binary;
using System.Text;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed record IcomLanRadioCapabilities(
    string RadioName, string AudioName, byte CivAddress,
    ushort CommonCapability, ushort RxSampleMask, ushort TxSampleMask,
    uint BaudRate, byte[] Guid, byte[] Mac);

public static partial class IcomLanControlCodec
{
    // Call only after MatchesCapabilities correlated the entire response to
    // the active login/token/SID transaction.
    public static bool TryParseCapabilities(ReadOnlySpan<byte> packet,
        out IcomLanRadioCapabilities capabilities)
    {
        capabilities = default!;
        const int header = 0x42, entryLength = 0x66;
        if (packet.Length < header + entryLength ||
            (packet.Length - header) % entryLength != 0)
            return false;
        for (int offset = header; offset + entryLength <= packet.Length;
            offset += entryLength)
        {
            byte civ = packet[offset + 0x52];
            if (civ == 0 || civ >= 0xE0) continue;
            var radio = packet.Slice(offset, entryLength);
            capabilities = new IcomLanRadioCapabilities(
                ReadAsciiZ(radio.Slice(0x10, 32)),
                ReadAsciiZ(radio.Slice(0x30, 32)),
                civ,
                BinaryPrimitives.ReadUInt16BigEndian(radio.Slice(7, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(radio.Slice(0x53, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(radio.Slice(0x55, 2)),
                BinaryPrimitives.ReadUInt32BigEndian(radio.Slice(0x5A, 4)),
                radio[..16].ToArray(),
                radio.Slice(0x0A, 6).ToArray());
            return true;
        }
        return false;
    }

    private static string ReadAsciiZ(ReadOnlySpan<byte> raw)
    {
        int nullIndex = raw.IndexOf((byte)0);
        return Encoding.ASCII.GetString(nullIndex >= 0 ? raw[..nullIndex] : raw);
    }
}
