using System.Buffers.Binary;
using SkyRoof.Remote.Foundation.Media;

internal static class IcomLanPacketTests
{
    public static void Run()
    {
        const uint sender = 0x12345678, receiver = 0xA0B0C0D0;
        byte[] pcm = [0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80, 0x34, 0x12];
        var rx = Packet(pcm, sender, receiver, 42, 17);
        Check(IcomLanAudioPacketCodec.TryDecode(rx, sender, receiver,
            IcomLanAudioCodec.Lpcm16Mono, out var decoded));
        Check(decoded.OuterSequence == 42 && decoded.AudioSequence == 17);
        Check(decoded.Pcm16.SequenceEqual(pcm));
        Check(!IcomLanAudioPacketCodec.TryDecode(rx, sender + 1, receiver,
            IcomLanAudioCodec.Lpcm16Mono, out _));
        Check(!IcomLanAudioPacketCodec.TryDecode(rx, sender, receiver + 1,
            IcomLanAudioCodec.Lpcm16Mono, out _));
        rx[4] = 1;
        Check(!IcomLanAudioPacketCodec.TryDecode(rx, sender, receiver,
            IcomLanAudioCodec.Lpcm16Mono, out _));
        rx[4] = 0;
        rx[0]--;
        Check(!IcomLanAudioPacketCodec.TryDecode(rx, sender, receiver,
            IcomLanAudioCodec.Lpcm16Mono, out _));

        var pcm8 = Packet([0, 128, 255], sender, receiver, 1, 2);
        Check(IcomLanAudioPacketCodec.TryDecode(pcm8, sender, receiver,
            IcomLanAudioCodec.Pcm8Mono, out var eight));
        Check(BinaryPrimitives.ReadInt16LittleEndian(eight.Pcm16.AsSpan(0, 2)) == -32768);
        Check(BinaryPrimitives.ReadInt16LittleEndian(eight.Pcm16.AsSpan(2, 2)) == 0);
        Check(BinaryPrimitives.ReadInt16LittleEndian(eight.Pcm16.AsSpan(4, 2)) == 32512);

        var ulaw = Packet([0xFF, 0x7F], sender, receiver, 1, 3);
        Check(IcomLanAudioPacketCodec.TryDecode(ulaw, sender, receiver,
            IcomLanAudioCodec.ULaw8Mono, out var companded));
        Check(companded.Pcm16.All(value => value == 0));

        short[] samples = [short.MinValue, 0, short.MaxValue];
        var encoded = IcomLanAudioPacketCodec.EncodeTransmit(samples, sender, receiver,
            33, 34, IcomLanAudioCodec.Lpcm16Mono);
        Check(encoded.Length == 30 && 
            BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(16, 2)) == 0x0080 &&
            BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(24, 2)) == short.MinValue &&
            BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(18, 2)) == 34);
        var special = IcomLanAudioPacketCodec.EncodeTransmit(new short[80], sender, receiver,
            1, 1, IcomLanAudioCodec.Lpcm16Mono);
        Check(BinaryPrimitives.ReadUInt16LittleEndian(special.AsSpan(16, 2)) == 0x9781);
        Console.WriteLine("PASS: IC-9700 LAN packet SIDs, sequence, PCM16/PCM8/u-law, TX framing.");
    }

    private static byte[] Packet(byte[] samples, uint sender, uint receiver,
        ushort outer, ushort audio)
    {
        byte[] packet = new byte[24 + samples.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), outer);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), sender);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), receiver);
        packet[16] = 0x80;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), audio);
        samples.CopyTo(packet, 24);
        return packet;
    }

    private static void Check(bool valid)
    {
        if (!valid) throw new InvalidOperationException("Icom LAN audio packet simulation failed.");
    }
}
