using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.Media;

public enum IcomLanAudioCodec : byte
{
    ULaw8Mono = 0x01,
    Pcm8Mono = 0x02,
    Lpcm16Mono = 0x04
}

public readonly record struct IcomLanRxAudioFrame(
    ushort OuterSequence, ushort AudioSequence, byte[] Pcm16);

/// <summary>
/// The RS-BA1-compatible 24-byte Icom LAN audio packet envelope.
/// The owning Direct LAN session supplies the negotiated sender/receiver SIDs
/// and codec; this codec does not authenticate to, or control, the radio.
/// </summary>
public static class IcomLanAudioPacketCodec
{
    public const int HeaderBytes = 24;

    public static bool TryDecode(ReadOnlySpan<byte> datagram,
        uint senderSid, uint receiverSid, IcomLanAudioCodec codec,
        out IcomLanRxAudioFrame frame)
    {
        frame = default;
        if (!Enum.IsDefined(codec) || datagram.Length <= HeaderBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(datagram[..4]) != (uint)datagram.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(4, 2)) == 0x0001 ||
            BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(8, 4)) != senderSid ||
            BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(12, 4)) != receiverSid ||
            datagram[16] is 0xC0 or 0xC1)
            return false;

        int payloadBytes = datagram.Length - HeaderBytes;
        int sampleBytes = codec == IcomLanAudioCodec.Lpcm16Mono ? 2 : 1;
        if (payloadBytes % sampleBytes != 0) return false;

        byte[] pcm = new byte[payloadBytes / sampleBytes * 2];
        if (codec == IcomLanAudioCodec.Lpcm16Mono)
            datagram[HeaderBytes..].CopyTo(pcm);
        else
            for (int i = 0; i < payloadBytes; i++)
            {
                short sample = codec == IcomLanAudioCodec.Pcm8Mono
                    ? (short)((datagram[HeaderBytes + i] - 128) * 256)
                    : DecodeULaw(datagram[HeaderBytes + i]);
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), sample);
            }

        frame = new IcomLanRxAudioFrame(
            BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(18, 2)), pcm);
        return true;
    }

    public static byte[] EncodeTransmit(ReadOnlySpan<short> samples,
        uint senderSid, uint receiverSid, ushort outerSequence,
        ushort audioSequence, IcomLanAudioCodec codec)
    {
        if (!Enum.IsDefined(codec)) throw new ArgumentOutOfRangeException(nameof(codec));
        int bytesPerSample = codec == IcomLanAudioCodec.Lpcm16Mono ? 2 : 1;
        int length = checked(samples.Length * bytesPerSample);
        if (length is < 1 or > 1364)
            throw new ArgumentOutOfRangeException(nameof(samples));

        byte[] packet = new byte[HeaderBytes + length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), outerSequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), senderSid);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), receiverSid);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16, 2),
            (ushort)(length == 160 ? 0x9781 : 0x0080));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), audioSequence);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22, 2), (ushort)length);

        for (int i = 0; i < samples.Length; i++)
            switch (codec)
            {
                case IcomLanAudioCodec.Lpcm16Mono:
                    BinaryPrimitives.WriteInt16LittleEndian(
                        packet.AsSpan(HeaderBytes + i * 2, 2), samples[i]);
                    break;
                case IcomLanAudioCodec.Pcm8Mono:
                    packet[HeaderBytes + i] = (byte)Math.Clamp(
                        (int)Math.Round(samples[i] / 256f + 128), 0, 255);
                    break;
                case IcomLanAudioCodec.ULaw8Mono:
                    packet[HeaderBytes + i] = EncodeULaw(samples[i]);
                    break;
            }
        return packet;
    }

    private static short DecodeULaw(byte value)
    {
        int u = ~value & 0xFF;
        int magnitude = ((u & 0x0F) << 3) + 0x84;
        magnitude <<= (u >> 4) & 0x07;
        return (short)((u & 0x80) != 0 ? 0x84 - magnitude : magnitude - 0x84);
    }

    private static byte EncodeULaw(short pcm)
    {
        int sample = pcm;
        int sign = sample < 0 ? 0x80 : 0;
        sample = Math.Min(32635, Math.Abs(sample) + 132);
        int exponent = 7;
        for (int mask = 0x4000; exponent > 0 && (sample & mask) == 0; mask >>= 1)
            exponent--;
        int mantissa = (sample >> (exponent + 3)) & 0x0F;
        return (byte)~(sign | (exponent << 4) | mantissa);
    }
}
