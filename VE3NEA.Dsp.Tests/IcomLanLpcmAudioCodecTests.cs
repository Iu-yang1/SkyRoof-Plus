using System.Buffers.Binary;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomLanLpcmAudioCodecTests
  {
    private const uint SenderSid = 0x01020304;
    private const uint ReceiverSid = 0xAABBCCDD;

    [Fact]
    public void DecodesSignedPcm16WithExactRsbA1HeaderAndSession()
    {
      byte[] packet = AudioPacket(
        77, 81, [0x00, 0x80, 0x00, 0x00, 0xFF, 0x7F]);
      Assert.True(IcomLanLpcmAudioCodec.TryDecode(
        packet, SenderSid, ReceiverSid, out var frame));
      Assert.Equal((ushort)77, frame.OuterSequence);
      Assert.Equal((ushort)81, frame.AudioSequence);
      Assert.Equal(3, frame.Samples.Length);
      Assert.Equal(-1f, frame.Samples[0]);
      Assert.Equal(0f, frame.Samples[1]);
      Assert.InRange(frame.Samples[2], 0.9999f, 1.0f);
    }

    [Fact]
    public void SignedPcmAmplitudeIsNormalizedWithoutChangingSampleCount()
    {
      short[] original = [-30000, -1024, -1, 0, 1, 1024, 30000];
      byte[] payload = new byte[original.Length * 2];
      for (int i = 0; i < original.Length; i++)
        BinaryPrimitives.WriteInt16LittleEndian(
          payload.AsSpan(i * 2, 2), original[i]);

      Assert.True(IcomLanLpcmAudioCodec.TryDecode(
        AudioPacket(0xFFFF, 0xFFFF, payload),
        SenderSid, ReceiverSid, out var frame));
      Assert.Equal(0xFFFF, frame.OuterSequence);
      Assert.Equal(0xFFFF, frame.AudioSequence);
      Assert.Equal(original.Length, frame.Samples.Length);
      for (int i = 0; i < original.Length; i++)
        Assert.Equal(original[i] / 32768f, frame.Samples[i]);
    }

    [Fact]
    public void RejectsMalformedLengthAndTruncatedPcm()
    {
      byte[] valid = AudioPacket(42, 24, [1, 0, 2, 0]);
      byte[] truncated = valid[..^1];
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        truncated, SenderSid, ReceiverSid, out _));

      byte[] wrongInnerLength = valid.ToArray();
      wrongInnerLength[23] = 0x06;
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        wrongInnerLength, SenderSid, ReceiverSid, out _));

      byte[] oddLength = AudioPacket(42, 24, [1, 2, 3]);
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        oddLength, SenderSid, ReceiverSid, out _));

      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        new byte[16], SenderSid, ReceiverSid, out _));
    }

    [Fact]
    public void RejectsOtherSessionAndNonAudioTransportPackets()
    {
      byte[] valid = AudioPacket(42, 24, [1, 0, 2, 0]);
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        valid, ReceiverSid, SenderSid, out _));
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        valid, SenderSid, 0, out _));

      byte[] retransmission = valid.ToArray();
      retransmission[4] = 0x01;
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        retransmission, SenderSid, ReceiverSid, out _));

      byte[] wrongIdent = valid.ToArray();
      wrongIdent[16] = 0xC1;
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        wrongIdent, SenderSid, ReceiverSid, out _));
    }

    [Fact]
    public void EmptyAudioAndOddSamplePayloadsAreNotFrames()
    {
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        AudioPacket(1, 1, []), SenderSid, ReceiverSid, out _));
      Assert.False(IcomLanLpcmAudioCodec.TryDecode(
        AudioPacket(2, 2, [0xFF]), SenderSid, ReceiverSid, out _));
    }

    private static byte[] AudioPacket(
      ushort outerSequence, ushort innerSequence, byte[] samples)
    {
      var datagram = new byte[IcomLanLpcmAudioCodec.HeaderBytes + samples.Length];
      BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(0, 4),
        (uint)datagram.Length);
      BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(6, 2),
        outerSequence);
      BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(8, 4), SenderSid);
      BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(12, 4), ReceiverSid);
      datagram[16] = 0x80;
      BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(18, 2),
        innerSequence);
      BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(22, 2),
        (ushort)samples.Length);
      samples.AsSpan().CopyTo(datagram.AsSpan(IcomLanLpcmAudioCodec.HeaderBytes));
      return datagram;
    }
  }
}
