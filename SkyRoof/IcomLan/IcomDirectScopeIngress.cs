using System.Threading.Channels;

namespace SkyRoof
{
  internal sealed class IcomDirectScopeIngress
  {
    private readonly Channel<byte[]> datagrams =
      Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
      {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
      });

    private long accepted;
    private long dropped;

    internal ChannelReader<byte[]> Reader => datagrams.Reader;
    internal long Accepted => Interlocked.Read(ref accepted);
    internal long Dropped => Interlocked.Read(ref dropped);
    internal int Queued => datagrams.Reader.Count;

    internal bool Offer(byte[] payload)
    {
      if (datagrams.Writer.TryWrite(payload))
      {
        Interlocked.Increment(ref accepted);
        return true;
      }

      Interlocked.Increment(ref dropped);
      return false;
    }

    internal void Complete() => datagrams.Writer.TryComplete();
  }

  internal sealed class IcomNativeCivSequenceTracker
  {
    private readonly HashSet<ushort> seen = new();
    private readonly Queue<ushort> order = new();
    private ushort? nextSequence;

    internal long MissingPackets { get; private set; }
    internal long Duplicates { get; private set; }
    internal long Reordered { get; private set; }

    internal bool Accept(ushort sequence)
    {
      if (!seen.Add(sequence))
      {
        Duplicates++;
        return false;
      }

      if (nextSequence is ushort next)
      {
        short distance = unchecked((short)(sequence - next));
        if (distance > 0)
        {
          MissingPackets += distance;
          nextSequence = unchecked((ushort)(sequence + 1));
        }
        else if (distance == 0)
          nextSequence = unchecked((ushort)(sequence + 1));
        else
          Reordered++;
      }
      else
        nextSequence = unchecked((ushort)(sequence + 1));

      order.Enqueue(sequence);
      if (order.Count > 512)
        seen.Remove(order.Dequeue());
      return true;
    }
  }

  internal static class IcomScopeRecoveryPolicy
  {
    internal static bool ShouldReassert(
      DateTime nowUtc, DateTime lastEnableUtc, DateTime lastWaveformUtc)
    {
      if (nowUtc - lastEnableUtc < TimeSpan.FromSeconds(8))
        return false;

      return lastWaveformUtc == default ||
        nowUtc - lastWaveformUtc >= TimeSpan.FromSeconds(2);
    }
  }
}
