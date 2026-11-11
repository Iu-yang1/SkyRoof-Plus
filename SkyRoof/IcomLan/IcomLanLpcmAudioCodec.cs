using System.Buffers.Binary;

namespace SkyRoof
{
  /// <summary>
  /// RX-only RS-BA1 audio datagram: 16-byte stream envelope, 8-byte audio
  /// header, little-endian signed PCM16. The sample rate is negotiated in
  /// conninfo and is not encoded in an individual audio datagram.
  ///
  /// This codec does not infer whether the radio selected AF or 12-kHz IF.
  /// That is an independent front-panel USB/WLAN/LAN Output Select setting.
  /// </summary>
  internal static class IcomLanLpcmAudioCodec
  {
    internal const int HeaderBytes = 24;

    internal readonly record struct RxFrame(
      ushort OuterSequence,
      ushort AudioSequence,
      float[] Samples);

    internal static bool TryDecode(
      ReadOnlySpan<byte> datagram,
      uint expectedSenderSid,
      uint expectedReceiverSid,
      out RxFrame frame)
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
          datagram[16] != 0x80 ||
          datagram[17] != 0x00)
        return false;

      int length = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(22, 2));
      if (length == 0 ||
          (length & 1) != 0 ||
          length != datagram.Length - HeaderBytes)
        return false;

      var samples = new float[length / 2];
      for (int i = 0; i < samples.Length; i++)
      {
        short pcm = BinaryPrimitives.ReadInt16LittleEndian(
          datagram.Slice(HeaderBytes + 2 * i, 2));
        samples[i] = pcm * (1f / 32768f);
      }

      frame = new RxFrame(
        BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(6, 2)),
        BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(18, 2)),
        samples);
      return true;
    }
  }
}
