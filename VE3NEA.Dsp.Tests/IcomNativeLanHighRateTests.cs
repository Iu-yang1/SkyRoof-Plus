using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomNativeLanHighRateTests
  {
    private static readonly DateTime Epoch =
      new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    private static IcomScopeFrame Frame(int index, byte receiver = 0) =>
      new()
      {
        Scope = receiver,
        Mode = (byte)IcomScopeMode.Center,
        FrequencyAHz = receiver == 0 ? 145_900_000 : 435_600_000,
        FrequencyBHz = 25_000,
        DivisionCurrent = 1,
        DivisionMaximum = 1,
        Samples = Enumerable.Repeat((byte)(index % 161), 475).ToArray(),
        SweepComplete = true,
        TimestampUtc = Epoch.AddMilliseconds(index * 30)
      };

    [Fact]
    public void ThirtyCompleteLanSweepsRetainLastTwelveForWaterfall()
    {
      var mailbox = new IcomScopeUiFrameMailbox(12);
      int callbacks = 0;
      for (int i = 1; i <= 30; i++)
        if (mailbox.Offer(Frame(i))) callbacks++;

      Assert.Equal(1, callbacks);
      Assert.Equal(18, mailbox.ReplacedCompleteSweeps);
      IcomScopeFrame[] delivered = mailbox.TakeAll();
      Assert.Equal(12, delivered.Length);
      Assert.All(delivered, frame =>
        Assert.True(frame.SweepComplete && frame.Samples.Length == 475));
      Assert.Equal(Frame(19).TimestampUtc, delivered[0].TimestampUtc);
      Assert.Equal(Frame(30).TimestampUtc, delivered[^1].TimestampUtc);
      Assert.Empty(mailbox.TakeAll());
    }

    [Fact]
    public void InterleavedMainSubBothRetainIndependentLanSweepHistory()
    {
      var mailbox = new IcomScopeUiFrameMailbox(4);
      for (int i = 1; i <= 6; i++)
      {
        mailbox.Offer(Frame(i * 2 - 1));
        mailbox.Offer(Frame(i * 2, receiver: 1));
      }
      IcomScopeFrame[] rows = mailbox.TakeAll();
      Assert.Equal(8, rows.Length);
      Assert.Equal(4, rows.Count(x => x.Scope == 0));
      Assert.Equal(4, rows.Count(x => x.Scope == 1));
      Assert.Equal(4, mailbox.ReplacedCompleteSweeps);
      Assert.True(rows.SequenceEqual(rows.OrderBy(x => x.TimestampUtc)));
    }

    [Fact]
    public void NativeIngressDoesNotSynchronouslyParseOrBlockUdpReceive()
    {
      var ingress = new IcomDirectScopeIngress();
      for (int index = 0; index < 300; index++)
        ingress.Offer(new byte[] { (byte)index });

      Assert.Equal(256, ingress.Accepted);
      Assert.Equal(44, ingress.Dropped);
      Assert.Equal(256, ingress.Queued);
      ingress.Complete();
      int expected = 0;
      while (ingress.Reader.TryRead(out byte[]? received))
        Assert.Equal(unchecked((byte)expected++), received[0]);
      Assert.Equal(256, expected);
    }

    [Fact]
    public void SerialOuterCounterReportsActualGapsWithoutDuplicateFrames()
    {
      var tracker = new IcomNativeCivSequenceTracker();
      Assert.True(tracker.Accept(65534));
      Assert.True(tracker.Accept(65535));
      Assert.True(tracker.Accept(0));
      Assert.True(tracker.Accept(2));
      Assert.Equal(1, tracker.MissingPackets);
      Assert.True(tracker.Accept(1));
      Assert.Equal(1, tracker.Reordered);
      Assert.False(tracker.Accept(1));
      Assert.Equal(1, tracker.Duplicates);
      Assert.True(tracker.Accept(3));
    }

    [Fact]
    public void FastSweepCommandsTargetOnlyActualReceivers()
    {
      byte[][] two = IcomLanDirectSession.BuildFastSweepCommands(0xA2, 2);
      Assert.Equal(2, two.Length);
      Assert.Equal(new byte[]
      {
        0xFE, 0xFE, 0xA2, 0xE0, 0x27, 0x1A, 0x00, 0x00, 0xFD
      }, two[0]);
      Assert.Equal(new byte[]
      {
        0xFE, 0xFE, 0xA2, 0xE0, 0x27, 0x1A, 0x01, 0x00, 0xFD
      }, two[1]);
      Assert.Single(IcomLanDirectSession.BuildFastSweepCommands(0xA4, 1));
      Assert.Single(IcomLanDirectSession.BuildFastSweepCommands(0xAC, 1));
    }

    [Fact]
    public void HealthyLanDataDisablesUnconditionalFiveSecondScopeReassert()
    {
      DateTime lastCommand = Epoch;
      DateTime lastFrame = Epoch.AddSeconds(7);
      Assert.False(IcomScopeRecoveryPolicy.ShouldReassert(
        Epoch.AddSeconds(9), lastCommand, lastFrame));
      Assert.False(IcomScopeRecoveryPolicy.ShouldReassert(
        Epoch.AddSeconds(6), lastCommand, default));
      Assert.True(IcomScopeRecoveryPolicy.ShouldReassert(
        Epoch.AddSeconds(10), lastCommand, Epoch.AddSeconds(7)));
      Assert.True(IcomScopeRecoveryPolicy.ShouldReassert(
        Epoch.AddSeconds(8), lastCommand, default));
    }

    [Fact]
    public void LegacySingleSweepMailboxStillCoalescesWhenNotOptedIn()
    {
      var mailbox = new IcomScopeUiFrameMailbox();
      Assert.True(mailbox.Offer(Frame(1)));
      Assert.False(mailbox.Offer(Frame(2)));
      Assert.Equal(1, mailbox.ReplacedCompleteSweeps);
      Assert.Single(mailbox.TakeAll());
    }
  }
}
