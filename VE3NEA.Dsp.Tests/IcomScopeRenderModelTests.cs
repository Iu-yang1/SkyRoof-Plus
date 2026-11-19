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
    public void GpuHistoryShiftOnlyChangesMetadataAndNeverResurrectsClippedBins()
    {
      var ring = new IcomScopeWaterfallRing();
      ring.Resize(40);
      ring.SetCpuRaster(false);
      var raw = Enumerable.Range(0, 475).Select(i => (byte)(i % 160 + 1)).ToArray();
      ring.Append(raw, 20);
      var first = ring.Snapshot();
      ring.Shift(-8);
      var shifted = ring.Snapshot();
      shifted.Rows[0].Samples.Should().BeSameAs(first.Rows[0].Samples);
      shifted.Rows[0].At(0).Should().Be(raw[8]);
      ring.Shift(8);
      var reverse = ring.Snapshot();
      reverse.Rows[0].Samples.Should().BeSameAs(first.Rows[0].Samples);
      reverse.Rows[0].At(0).Should().Be(0);
      reverse.Rows[0].At(8).Should().Be(raw[8]);
      reverse.Colors.Should().BeEmpty();
      ring.SetCpuRaster(true);
      var software = ring.Snapshot();
      software.Colors.Should().HaveCount(40);
      software.Colors[0][8].Should().Be(software.ColorTable[20 * 161 + raw[8]]);
    }

    [Theory]
    [InlineData(240, 136)]
    [InlineData(800, 120)]
    public void CompactWaterfallHistoryDoesNotResampleOldRowsOnEverySweep(int rows, int height)
    {
      var raster = new IcomScopeWaterfallTimeRaster(height);
      var source = Enumerable.Range(0, rows).Select(i =>
        Enumerable.Repeat(unchecked((int)0xff000000) | i, 475).ToArray()).ToArray();
      var first = raster.Update(source, 0);
      for (int n = 1; n <= rows * 2; n++)
      {
        int head = (rows - n % rows) % rows;
        source = (int[][])source.Clone();
        source[head] = Enumerable.Repeat(unchecked((int)0xff000000) | (n + rows), 475).ToArray();
        var next = raster.Update(source, head);
        int scroll = (first.Head - next.Head + height) % height;
        scroll.Should().BeInRange(0, 1);
        for (int y = 1 + scroll; y < height; y++)
          next.Rows[(next.Head + y) % height].Should().BeSameAs(
            first.Rows[(first.Head + y - scroll) % height]);
        first = next;
      }
    }

    [Fact]
    public void CompactTimeRasterUsesAllSweepsInCompletedGroup()
    {
      var source = Enumerable.Range(0, 4).Select(i =>
        Enumerable.Repeat(unchecked((int)0xff000000), 475).ToArray()).ToArray();
      source[0] = Enumerable.Repeat(unchecked((int)0xff000020), 475).ToArray();
      source[3] = Enumerable.Repeat(unchecked((int)0xff000060), 475).ToArray();
      var result = new IcomScopeWaterfallTimeRaster(2).Update(source, 3);
      result.Rows[result.Head][0].Should().Be(unchecked((int)0xff000040));
    }

    [Fact]
    public void AvgFourRemainsStableAtFixedCenterFrequency()
    {
      var model = new IcomScopeRenderModel();
      model.SetAverage(4);
      for (int i = 1; i <= 32; i++)
      {
        model.Push(Frame(i, level: (byte)(i % 2 == 0 ? 100 : 20)));
        if (i < 4) continue;
        model.AverageDepth.Should().Be(4);
        model.Snapshot().Samples[237].Should().Be(60);
      }
    }

    [Fact]
    public void AvgFourDoesNotRestartWithEverySubBinDopplerChange()
    {
      var model = new IcomScopeRenderModel();
      model.SetAverage(4);
      for (int i = 1; i <= 12; i++)
      {
        byte value = (byte)(i % 2 == 0 ? 100 : 20);
        model.Push(Frame(i, 435000000 + i, level: value));
        var snapshot = model.Snapshot();
        if (i >= 4)
        {
          model.AverageDepth.Should().Be(4);
          snapshot.Samples[237].Should().Be(60);
        }
      }
    }

    [Fact]
    public void AverageMovesWithWholeBinDopplerAndDoesNotAttenuateUnobservedEdges()
    {
      var avg = new IcomScopeRollingAverage();
      var output = new byte[475];
      byte[] old = Enumerable.Repeat((byte)40, 475).ToArray();
      old[237] = 160;
      avg.Append(old, 4, output);
      avg.Shift(-1);
      byte[] next = Enumerable.Repeat((byte)80, 475).ToArray();
      next[236] = 120;
      avg.Append(next, 4, output);
      output[236].Should().Be(140);
      output[100].Should().Be(60);
      output[474].Should().Be(80);
      avg.Shift(1);
      avg.Append(next, 4, output);
      // The first historical row lost its leftmost bin permanently.
      output[0].Should().Be(80);
      avg.Count.Should().Be(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WaterfallHistoryRetainsItsOwnNoiseFloorThroughShiftAndRecolor(bool cpuRaster)
    {
      var ring = new IcomScopeWaterfallRing();
      ring.Resize(40);
      ring.SetCpuRaster(cpuRaster);
      var samples = Enumerable.Repeat((byte)60, 475).ToArray();
      ring.Append(samples, 20);
      var first = ring.Snapshot();
      int firstColor = first.ColorTable[20 * 161 + 60];
      ring.Shift(-1);
      ring.Append(samples, 40);
      var next = ring.Snapshot();
      next.Rows[0].Floor.Should().Be(20);
      next.Rows[next.Head].Floor.Should().Be(40);
      next.Rows[0].At(100).Should().Be(60);
      if (cpuRaster) next.Colors[0][100].Should().Be(firstColor);
      ring.SetDisplay(15, 120, IcomScopeWaterfallPalette.Classic);
      var recolored = ring.Snapshot();
      recolored.Rows[0].Floor.Should().Be(20);
      recolored.Rows[recolored.Head].Floor.Should().Be(40);
      if (cpuRaster) recolored.Colors[0][100].Should().Be(recolored.ColorTable[20 * 161 + 60]);
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
    public void CaptureRoutingProcessesEverySweepWithoutUiDrain()
    {
      var router = new IcomScopeFrameRouter();
      using var worker = new IcomScopeRenderWorker(() => { });
      var metadata = new IcomScopeUiFrameMailbox();
      router.Start(1, () => worker.Configure(m => m.Clear(1)));
      for (int i = 1; i <= 180; i++)
        router.Offer(1, Frame(i), worker.Offer, metadata.Offer);
      SpinWait.SpinUntil(() => worker.ProcessedSweeps == 180 && worker.Latest?.Frame?.TimestampUtc == Frame(180).TimestampUtc,
        TimeSpan.FromSeconds(15)).Should().BeTrue();
      metadata.ReplacedCompleteSweeps.Should().Be(179);
      worker.DroppedSweeps.Should().Be(0);
      router.SetHold(true);
      router.Offer(1, Frame(181), worker.Offer, metadata.Offer).Should().BeFalse();
      router.SetHold(false);
      router.Stop(2, () => worker.Configure(m => m.Clear(2)));
      router.Offer(1, Frame(182), worker.Offer, metadata.Offer).Should().BeFalse();
      SpinWait.SpinUntil(() => worker.Latest?.Generation == 2, TimeSpan.FromSeconds(5)).Should().BeTrue();
      worker.Latest?.Frame.Should().BeNull();
      worker.ProcessedSweeps.Should().Be(180);
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
      var result = worker.Latest ?? throw new InvalidOperationException("No published snapshot");
      result.Colors.Should().HaveCount(800);
      result.Head.Should().Be(621);
    }
  }
}
