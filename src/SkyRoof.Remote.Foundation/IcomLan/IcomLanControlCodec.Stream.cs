using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed record IcomLanStreamOptions(
    uint LocalSid, uint RemoteSid, ushort InnerSequence, byte[] AuthId,
    string RadioName, string Username, ushort CommonCapability,
    byte[] RadioGuid, byte[] RadioMac, ushort RxSampleCapabilities,
    int LocalCivPort, int LocalAudioPort,
    byte RxCodec = 4, uint RxSampleRate = 0,
    byte TxCodec = 0, uint TxSampleRate = 0, uint TxBufferMs = 0,
    bool RxEnabled = true);

public static partial class IcomLanControlCodec
{
    public static byte[] BuildStreamRequest(IcomLanStreamOptions options)
    {
        if (options.AuthId.Length != 6 || options.RadioGuid.Length != 16 ||
            options.RadioMac.Length != 6)
            throw new ArgumentException("Invalid radio capability identity lengths.");
        if (options.LocalCivPort is < 1 or > 65535 ||
            options.LocalAudioPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options));
        byte[] packet = new byte[0x90];
        BuildRequestHeader(packet, options.LocalSid, options.RemoteSid,
            options.InnerSequence, 3);
        options.AuthId.CopyTo(packet, 0x1A);
        if (options.CommonCapability == 0x8010)
        {
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0x27, 2), 0x8010);
            options.RadioMac.CopyTo(packet, 0x2A);
        }
        else options.RadioGuid.CopyTo(packet, 0x20);
        WriteAsciiZ(packet.AsSpan(0x40, 32), options.RadioName);
        EncodePasscode(options.Username).CopyTo(packet, 0x60);

        uint rxRate = !options.RxEnabled ? 0
            : options.RxSampleRate != 0 ? options.RxSampleRate
            : SelectReceiveSampleRate(options.RxSampleCapabilities);
        packet[0x70] = rxRate == 0 ? (byte)0 : (byte)1;
        packet[0x71] = options.TxSampleRate == 0 ? (byte)0 : (byte)1;
        packet[0x72] = rxRate == 0 ? (byte)0 : options.RxCodec;
        packet[0x73] = options.TxSampleRate == 0 ? (byte)0 : options.TxCodec;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0x74, 4), rxRate);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0x78, 4),
            options.TxSampleRate);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0x7C, 4),
            (uint)options.LocalCivPort);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0x80, 4),
            (uint)options.LocalAudioPort);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0x84, 4),
            options.TxBufferMs);
        packet[0x88] = 1;
        return packet;
    }

    public static uint SelectReceiveSampleRate(ushort capabilities) => SelectSampleRate(capabilities, 0);

    public static uint SelectSampleRate(ushort capabilities, uint preferred)
    {
        ushort preferredBit = preferred switch
        {
            48000 => 8, 32000 => 4, 16000 => 2, 12000 => 0x80,
            44100 => 0x40, 22050 => 0x20, 11025 => 0x10, 8000 => 1, _ => 0
        };
        if ((capabilities & preferredBit) != 0) return preferred;
        foreach (var (bit, rate) in new (ushort, uint)[]
        {
            (0x0008, 48000), (0x0004, 32000), (0x0002, 16000),
            (0x0080, 12000), (0x0040, 44100), (0x0020, 22050),
            (0x0010, 11025), (0x0001, 8000)
        })
            if ((capabilities & bit) != 0) return rate;
        return 0;
    }
}
