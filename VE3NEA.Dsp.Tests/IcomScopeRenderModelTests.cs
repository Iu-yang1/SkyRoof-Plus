using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomScopeRenderModelTests
  {
    private static IcomScopeFrame Frame(int ms, long center = 435000000, int peak = 237,
      bool complete = true, byte level = 100)
    {
      var data = new byte[475];
      data[peak] = level;
      return new IcomScopeFrame
      {
        TimestampUtc = DateTime.UnixEpoch.AddMilliseconds(ms),
        Mode = (byte)IcomScopeMode.Center,
        FrequencyAHz = center, FrequencyBHz = 47400,
        Samples = data, SweepComplete = complete
      };
    }

    [Fact]
    public void CompleteSweepsOnlyAdvanceHistoryAndSnapshotsRemainImmutable()
    {
      var model = new IcomScopeRenderModel();
      model.SetHistoryRows(40);
      model.Push(Frame(1));
      var first = model.Snapshot();
      model.Push(Frame(2, complete: false, level: 80));
      var partial = model.Snapshot();
      partial.Head.Should().Be(first.Head);
      partial.Colors.Should().BeSameAs(first.Colors);
      partial.Samples[237].Should().Be(80);
      model.Push(Frame(3));
      var second = model.Snapshot();
      second.Head.Should().Be(39);
      second.Colors[0].Should().BeSameAs(first.Colors[0]);
      first.Samples[237].Should().Be(100);
    }

    [Fact]
    public void DopplerShiftMovesOldSignalToCorrectBin()
    {
      var model = new IcomScopeRenderModel();
      model.SetHistoryRows(40);
      model.Push(Frame(1));
      var first = model.Snapshot();
      model.Push(Frame(2, 435000100, 236));
      var second = model.Snapshot();
      second.Colors[0][236].Should().Be(first.Colors[0][237]);
      second.Colors[0][237].Should().Be(first.Colors[0][236]);
      first.Colors[0][237].Should().NotBe(first.Colors[0][236]);
    }

    [Fact]
    public void AveragingPeakAndResetPreserveRawDataAndGeneration()
    {
      var model = new IcomScopeRenderModel();
      model.SetAverage(2);
      model.SetPeak(true);
      model.Push(Frame(1, level: 100));
      model.Push(Frame(2, level: 60));
      var averaged = model.Snapshot();
      averaged.Samples[237].Should().Be(80);
      averaged.Peak[237].Should().Be(100);
      model.Clear(7);
      var cleared = model.Snapshot();
      cleared.Generation.Should().Be(7);
      cleared.Head.Should().Be(-1);
      cleared.Frame.Should().BeNull();
      cleared.PeakValid.Should().BeFalse();
    }

    [Fact]
    public void WorkerProcessesCompleteSweepsWhileConsumerDoesNotPaint()
    {
      using var worker = new IcomScopeRenderWorker(() => { });
      worker.Configure(m => m.SetHistoryRows(800));
      for (int i = 1; i <= 180; i++) worker.Offer(Frame(i, 435000000 + i * 100, 237));
      worker.Configure(m => m.SetPeak(true));
      SpinWait.SpinUntil(() => worker.Latest?.Frame?.TimestampUtc == Frame(180).TimestampUtc,
        TimeSpan.FromSeconds(15)).Should().BeTrue();
      worker.DroppedSweeps.Should().Be(0);
      worker.Latest!.Colors.Should().HaveCount(800);
      worker.Latest.Head.Should().Be(621);
    }
  }
}
