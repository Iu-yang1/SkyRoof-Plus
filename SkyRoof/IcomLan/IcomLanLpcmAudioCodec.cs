using System.Buffers.Binary;

namespace SkyRoof
{
  /// <summary>
  /// Icom RS-BA1-compatible LAN audio packets: 16-byte session envelope,
  /// 8-byte audio header and negotiated mono PCM16/PCM8/u-law payload.
  /// Audio content (AF or 12-kHz IF) is selected at the radio, not in UDP.
  /// </summary>
  internal static class IcomLanLpcmAudioCodec
  {
    internal const int HeaderBytes = 24;

    internal readonly record struct RxFrame(
      ushort OuterSequence, ushort AudioSequence, float[] Samples);

    internal static bool TryDecode(
      ReadOnlySpan<byte> datagram, uint expectedSenderSid,
      uint expectedReceiverSid, out RxFrame frame) =>
      TryDecode(datagram, expectedSenderSid, expectedReceiverSid,
        IcomLanAudioCodec.Lpcm16Mono, out frame);

    internal static bool TryDecode(
      ReadOnlySpan<byte> datagram, uint expectedSenderSid,
      uint expectedReceiverSid, IcomLanAudioCodec codec, out RxFrame frame)
    {
      frame = default;
      if (datagram.Length <= HeaderBytes ||
          BinaryPrimitives.ReadUInt32LittleEndian(datagram[..4]) !=
            (uint)datagram.Length ||
          BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(4, 2)) != 0 ||
          BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(8, 4)) !=
            expectedSenderSid ||
          BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(12, 4)) !=
            expectedReceiverSid ||
          !KnownAudioIdentifier(datagram[16], datagram[17]))
        return false;

      int length = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(22, 2));
      int bytesPerSample = codec == IcomLanAudioCodec.Lpcm16Mono ? 2 : 1;
      if (length == 0 || length % bytesPerSample != 0 ||
          length != datagram.Length - HeaderBytes ||
          !Enum.IsDefined(codec))
        return false;

      var samples = new float[length / bytesPerSample];
      for (int i = 0; i < samples.Length; i++)
      {
        samples[i] = codec switch
        {
          IcomLanAudioCodec.Lpcm16Mono =>
            BinaryPrimitives.ReadInt16LittleEndian(
              datagram.Slice(HeaderBytes + 2 * i, 2)) / 32768f,
          IcomLanAudioCodec.Pcm8Mono =>
            (datagram[HeaderBytes + i] - 128) / 128f,
          IcomLanAudioCodec.ULaw8Mono =>
            DecodeULaw(datagram[HeaderBytes + i]) / 32768f,
          _ => 0
        };
      }

      frame = new RxFrame(
        BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(6, 2)),
        BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(18, 2)),
        samples);
      return true;
    }

    // Identifiers differ between Icom audio codecs and initial/subsequent
    // packets. Never mistake ping/control packets for audio data.
    private static bool KnownAudioIdentifier(byte lo, byte hi) =>
      (lo == 0x80 && hi == 0x00) ||
      (lo == 0x44 && hi == 0x02) ||
      (lo == 0x6C && hi == 0x05) ||
      (lo == 0x70 && hi == 0x04) ||
      (lo == 0xD8 && hi == 0x03);

    internal static byte[] EncodeTransmit(
      ReadOnlySpan<float> samples,
      uint senderSid, uint receiverSid,
      ushort outerSequence, ushort audioSequence,
      IcomLanAudioCodec codec)
    {
      if (!Enum.IsDefined(codec))
        throw new ArgumentOutOfRangeException(nameof(codec));
      int bytesPerSample = codec == IcomLanAudioCodec.Lpcm16Mono ? 2 : 1;
      int length = checked(samples.Length * bytesPerSample);
      if (length == 0 || length > 1364)
        throw new ArgumentOutOfRangeException(nameof(samples));
      byte[] packet = new byte[HeaderBytes + length];
      BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4),
        (uint)packet.Length);
      BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2),
        outerSequence);
      BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), senderSid);
      BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), receiverSid);
      // The Icom transmit-audio identifier is little-endian 0x0080,
      // except for its special 160-byte payload variant.
      BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16, 2),
        (ushort)(length == 160 ? 0x9781 : 0x0080));
      BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2),
        audioSequence);
      BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22, 2),
        (ushort)length);
      for (int i = 0; i < samples.Length; i++)
      {
        short pcm = (short)Math.Clamp((int)Math.Round(
          Math.Clamp(samples[i], -1f, 1f) * 32767), -32768, 32767);
        switch (codec)
        {
          case IcomLanAudioCodec.Lpcm16Mono:
            BinaryPrimitives.WriteInt16LittleEndian(
              packet.AsSpan(HeaderBytes + i * 2, 2), pcm);
            break;
          case IcomLanAudioCodec.Pcm8Mono:
            packet[HeaderBytes + i] = (byte)Math.Clamp(
              (int)Math.Round(pcm / 256f + 128), 0, 255);
            break;
          case IcomLanAudioCodec.ULaw8Mono:
            packet[HeaderBytes + i] = EncodeULaw(pcm);
            break;
        }
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
      for (int mask = 0x4000; exponent > 0 && (sample & mask) == 0;
           mask >>= 1) exponent--;
      int mantissa = (sample >> (exponent + 3)) & 0x0F;
      return (byte)~(sign | (exponent << 4) | mantissa);
    }
  }
}
