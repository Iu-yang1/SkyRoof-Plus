using System.Buffers.Binary;
using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomDirectLanAudioTests
  {
    [Fact]
    public void StreamRequest_EnablesRxTxLpcmAndSetsTxPrebuffer()
    {
      byte[] authId = [1, 2, 3, 4, 5, 6];
      byte[] guid = new byte[16];
      byte[] mac = new byte[6];
      byte[] packet = IcomLanDirectSession.BuildStreamRequestPacket(
        0x11223344, 0xAABBCCDD, 7,
        authId, "IC-9700", "radio",
        0, guid, mac, 0x0008, 41000, 41001,
        rxCodec: 0x04, rxSampleRate: 48000,
        txCodec: 0x04, txSampleRate: 48000,
        txBufferMs: 150);
      packet[0x70].Should().Be(1);
      packet[0x71].Should().Be(1);
      packet[0x72].Should().Be(4);
      packet[0x73].Should().Be(4);
      BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(0x74, 4))
        .Should().Be(48000);
      BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(0x78, 4))
        .Should().Be(48000);
      BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(0x84, 4))
        .Should().Be(150);
    }

    [Theory]
    [InlineData(IcomLanAudioCodec.Lpcm16Mono)]
    [InlineData(IcomLanAudioCodec.Pcm8Mono)]
    [InlineData(IcomLanAudioCodec.ULaw8Mono)]
    public void TxAudioEncodeDecode_PreservesNegotiatedMonoAudio(
      IcomLanAudioCodec codec)
    {
      const uint client = 0x01020304;
      const uint radio = 0x0A0B0C0D;
      float[] original = [0f, 0.5f, -0.75f, 0.1f, -0.1f];
      byte[] packet = IcomLanLpcmAudioCodec.EncodeTransmit(
        original, client, radio, 0x1020, 0x3040, codec);
      BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4))
        .Should().Be((uint)packet.Length);
      BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6, 2))
        .Should().Be(0x1020);
      BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(18, 2))
        .Should().Be(0x3040);
      IcomLanLpcmAudioCodec.TryDecode(
        packet, client, radio, codec, out var decoded).Should().BeTrue();
      decoded.Samples.Length.Should().Be(original.Length);
      for (int i = 0; i < original.Length; i++)
        decoded.Samples[i].Should().BeApproximately(original[i], 0.04f);
      IcomLanLpcmAudioCodec.TryDecode(
        packet, radio, client, codec, out _).Should().BeFalse();
    }

    [Fact]
    public void TxAudioNotQueuedWithoutAuthenticatedSessionAndPtt()
    {
      using var session = new IcomLanDirectSession(
        "192.0.2.1", 50001, "operator", "test-password", "SkyRoof",
        audioSettings: new IcomDirectLanSettings
        {
          Transmit = new IcomDirectLanTransmitSettings { Enabled = true }
        })
      {
        IsPttActive = () => true
      };
      session.TryQueueTransmitAudio([0.1f], 1).Should().BeFalse();
    }

    [Fact]
    public void AudioSampleRateFollowsRadioCapabilities()
    {
      IcomLanDirectSession.ChooseSampleRate(
        IcomLanAudioSampleRate.Hz32000, 0x000C).Should().Be(32000);
      IcomLanDirectSession.ChooseSampleRate(
        IcomLanAudioSampleRate.Hz22050, 0x0008).Should().Be(48000);
      IcomLanDirectSession.ChooseSampleRate(
        IcomLanAudioSampleRate.Hz48000, 0).Should().Be(0);
    }
  }
}
