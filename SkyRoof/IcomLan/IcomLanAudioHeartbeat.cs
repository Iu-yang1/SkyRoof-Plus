using System.Buffers.Binary;

namespace SkyRoof
{
  /// <summary>
  /// Observes radio-originated audio control traffic independently from
  /// successful local UDP sends. Only replies to an outstanding random
  /// keepalive or requests with matching session IDs confirm the radio.
  /// </summary>
  internal sealed class IcomLanAudioHeartbeat
  {
    private readonly object sync = new();
    private readonly Dictionary<ushort, byte> pending = new();
    private readonly Queue<ushort> order = new();
    private long lastRemoteTicks;
    private long matchedReplies;

    internal long LastRemoteTicks => Interlocked.Read(ref lastRemoteTicks);
    internal long MatchedReplies => Interlocked.Read(ref matchedReplies);

    internal void RecordSent(ushort innerSequence, byte nonce)
    {
      lock (sync)
      {
        pending[innerSequence] = nonce;
        order.Enqueue(innerSequence);
        while (order.Count > 128)
          pending.Remove(order.Dequeue());
      }
    }

    internal bool Observe(ReadOnlySpan<byte> packet,
      uint localSid, uint remoteSid, long utcTicks)
    {
      if (packet.Length != 21 ||
          BinaryPrimitives.ReadUInt32LittleEndian(packet[..4]) != 21 ||
          BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(4, 2)) != 7 ||
          BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(8, 4)) != remoteSid ||
          BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(12, 4)) != localSid ||
          packet[20] != 0x06)
        return false;

      if (packet[16] == 0)
      {
        Interlocked.Exchange(ref lastRemoteTicks, utcTicks);
        return true; // Correlated radio-initiated ping, answered by the caller.
      }

      if (packet[16] != 1) return false;
      ushort inner = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(18, 2));
      bool matched;
      lock (sync)
        matched = pending.TryGetValue(inner, out byte nonce) &&
          nonce == packet[17] && pending.Remove(inner);

      if (!matched) return false;
      Interlocked.Increment(ref matchedReplies);
      Interlocked.Exchange(ref lastRemoteTicks, utcTicks);
      return true;
    }

    internal bool HasGoneStale(long nowTicks, long connectedTicks,
      long transmittedDatagrams, TimeSpan timeout)
    {
      if (transmittedDatagrams < 20 || connectedTicks <= 0)
        return false;

      long baseline = Math.Max(connectedTicks, LastRemoteTicks);
      return nowTicks >= baseline &&
        nowTicks - baseline >= timeout.Ticks;
    }
  }
}
