using System.Buffers.Binary;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomLanAudioHeartbeatTests
  {
    private const uint LocalSid = 0x11223344;
    private const uint RemoteSid = 0xAABBCCDD;

    [Fact]
    public void KeepaliveReplyMustMatchLiveAudioSessionAndOutstandingNonce()
    {
      var heartbeat = new IcomLanAudioHeartbeat();
      long start = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc).Ticks;
      heartbeat.RecordSent(0x8304, 0x5A);

      byte[] valid = BuildAudioPing(1, 0x8304, 0x5A);
      byte[] wrongNonce = valid.ToArray();
      wrongNonce[17] ^= 0x01;
      Assert.False(heartbeat.Observe(wrongNonce, LocalSid, RemoteSid, start));
      Assert.Equal(0, heartbeat.LastRemoteTicks);

      byte[] wrongSid = valid.ToArray();
      wrongSid[8] ^= 0x01;
      Assert.False(heartbeat.Observe(wrongSid, LocalSid, RemoteSid, start));

      Assert.True(heartbeat.Observe(valid, LocalSid, RemoteSid, start + 5));
      Assert.Equal(start + 5, heartbeat.LastRemoteTicks);
      Assert.Equal(1, heartbeat.MatchedReplies);
      Assert.False(heartbeat.Observe(valid, LocalSid, RemoteSid, start + 10));
      Assert.Equal(1, heartbeat.MatchedReplies);
      Assert.Equal(start + 5, heartbeat.LastRemoteTicks);
    }

    [Fact]
    public void RadioOriginatedPingConfirmsRemoteContactWithoutOngoingTx()
    {
      var heartbeat = new IcomLanAudioHeartbeat();
      byte[] ping = BuildAudioPing(0, 0x9100, 0x72);
      long now = DateTime.UtcNow.Ticks;
      Assert.True(heartbeat.Observe(ping, LocalSid, RemoteSid, now));
      Assert.Equal(now, heartbeat.LastRemoteTicks);
      Assert.Equal(0, heartbeat.MatchedReplies);
      ping[20] = 0;
      Assert.False(heartbeat.Observe(ping, LocalSid, RemoteSid, now + 2));
    }

    [Fact]
    public void StaleDetectorRequiresActualTransmittedFramesAndElapsedTimeout()
    {
      var heartbeat = new IcomLanAudioHeartbeat();
      long connected = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc).Ticks;
      TimeSpan timeout = TimeSpan.FromSeconds(12);
      Assert.False(heartbeat.HasGoneStale(
        connected + timeout.Ticks, connected, 0, timeout));
      Assert.False(heartbeat.HasGoneStale(
        connected + timeout.Ticks, connected, 19, timeout));
      Assert.False(heartbeat.HasGoneStale(
        connected + timeout.Ticks - 1, connected, 20, timeout));
      Assert.True(heartbeat.HasGoneStale(
        connected + timeout.Ticks, connected, 20, timeout));

      heartbeat.RecordSent(8, 7);
      Assert.True(heartbeat.Observe(
        BuildAudioPing(1, 8, 7), LocalSid, RemoteSid,
        connected + TimeSpan.FromSeconds(10).Ticks));
      Assert.False(heartbeat.HasGoneStale(
        connected + TimeSpan.FromSeconds(19).Ticks, connected, 200, timeout));
      Assert.True(heartbeat.HasGoneStale(
        connected + TimeSpan.FromSeconds(22).Ticks, connected, 200, timeout));
    }

    [Fact]
    public void DisconnectPacketUsesProtocolTypeFiveAndCurrentStreamIds()
    {
      byte[] packet = IcomLanDirectSession.BuildDisconnectPacket(LocalSid, RemoteSid);
      Assert.Equal(16, packet.Length);
      Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)));
      Assert.Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)));
      Assert.Equal(LocalSid, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4)));
      Assert.Equal(RemoteSid, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(12, 4)));
      Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6, 2)));
    }

    private static byte[] BuildAudioPing(byte direction, ushort inner, byte nonce)
    {
      var packet = new byte[21];
      BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 21);
      BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 7);
      BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), RemoteSid);
      BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), LocalSid);
      packet[16] = direction;
      packet[17] = nonce;
      BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(18, 2), inner);
      packet[20] = 0x06;
      return packet;
    }
  }
}
