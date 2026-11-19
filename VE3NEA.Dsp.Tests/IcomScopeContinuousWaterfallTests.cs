using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class IcomScopeContinuousWaterfallTests
{
  private const int Bins = 475;
  private static int[] Row(int v) => Enumerable.Repeat(unchecked((int)0xff000000) | v, Bins).ToArray();

  [Theory]
  [InlineData(240, 136)]
  [InlineData(800, 120)]
  [InlineData(240, 480)]
  public void ArrivalAndRingWrapDoNotChangeAlreadyPresentedPixels(int rows, int height)
  {
    var source = Enumerable.Range(0, rows).Select(i => Row((i * 17) & 255)).ToArray();
    var previous = new int[height * Bins];
    var next = new int[height * Bins];
    int head = 0;
    for (int n = 0; n < rows * 2; n++)
    {
      IcomScopeContinuousRaster.Render(source, head, 0, previous, height);
      head = (head + rows - 1) % rows;
      source[head] = Row((n * 97) & 255);
      IcomScopeContinuousRaster.Render(source, head, 1, next, height);
      Assert.True(previous.AsSpan().SequenceEqual(next), $"arrival {n}, head {head}");
    }
  }

  [Fact]
  public void HalfSweepMovesHistoryContinuouslyWithoutInventingRawRows()
  {
    int[][] source = [Row(0), Row(100), Row(200), Row(40)];
    var pixels = new int[Bins * 3];
    IcomScopeContinuousRaster.Render(source, 0, 0.5, pixels, 3);
    Assert.Equal(50, pixels[0] & 255);
    Assert.Equal(150, pixels[Bins] & 255);
    Assert.Equal(120, pixels[2 * Bins] & 255);
    Assert.Equal(120, pixels[^1] & 255); // scalar tail matches SIMD
    Assert.Equal(0, source[0][0] & 255);
  }

  [Theory]
  [InlineData(240, 136)]
  [InlineData(800, 120)]
  public void ConstantColorHasNoGroupDependentGain(int rows, int height)
  {
    int color = unchecked((int)0xfff19367);
    int[][] source = Enumerable.Range(0, rows).Select(_ => Row(color)).ToArray();
    var output = new int[Bins * height];
    foreach (double lag in new[] { 0.0, 0.125, 0.5, 0.875, 1.0 })
    {
      IcomScopeContinuousRaster.Render(source, rows - 1, lag, output, height);
      Assert.All(output, pixel => Assert.Equal(color, pixel));
    }
  }

  [Fact]
  public void ClockIsMonotonicAtSixtyHzWithEarlyLateSweepsAndHold()
  {
    var ring = new IcomScopeWaterfallRing();
    ring.Resize(40);
    ring.Append(new byte[Bins], 0);
    var first = ring.Snapshot() with { SweepTick = 0 };
    var clock = new IcomScopeWaterfallClock();
    long half = Stopwatch.Frequency / 60;
    Assert.Equal(1, clock.Lag(first, 0), 6);
    Assert.Equal(0.5, clock.Lag(first, half), 5);
    Assert.Equal(0.5, clock.Lag(first, 2 * half, held: true), 5);
    ring.Append(new byte[Bins], 0);
    var early = ring.Snapshot() with { SweepTick = half };
    Assert.Equal(1, clock.Lag(early, half), 5);
    Assert.Equal(0, clock.Lag(early, 4 * half), 5);
    Assert.Equal(0, clock.Lag(early, 30 * half), 5);
    ring.Clear();
    Assert.Equal(0, clock.Lag(ring.Snapshot(), 30 * half));
  }

  [Fact]
  public void FractionalFrequencyOffsetIsEncodedAndRawSignalRemainsImmutable()
  {
    var ring = new IcomScopeWaterfallRing();
    ring.Resize(40);
    var raw = new byte[Bins];
    raw[237] = 160;
    ring.Append(raw, 0);
    var original = ring.Snapshot();
    int high = original.Colors[0][237], low = original.Colors[0][236];
    ring.ShiftFractional(-0.25);
    var shifted = ring.Snapshot();
    Assert.Same(original.Rows[0].Samples, shifted.Rows[0].Samples);
    Assert.Equal(-0.25, shifted.Rows[0].EffectiveOffset, 8);
    Assert.Equal(IcomScopeContinuousRaster.Blend(low, high, 64), shifted.Colors[0][236]);
    Assert.Equal(IcomScopeContinuousRaster.Blend(high, low, 64), shifted.Colors[0][237]);
    Span<int> meta = stackalloc int[4];
    IcomScopeTextureTransfer.EncodeMetadata(shifted.Rows[0], meta);
    Assert.Equal(-0.25f, BitConverter.Int32BitsToSingle(meta[0]));
    ring.ShiftFractional(0.25);
    Assert.Equal(high, ring.Snapshot().Colors[0][237]);
  }

  [Fact]
  public void SoftwareDibActuallyDrawsInterpolatedRows()
  {
    using var history = new IcomScopeGdiHistoryBitmap(4);
    history.Update([Row(0), Row(100), Row(200), Row(40)]);
    using var target = new Bitmap(Bins, 3, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(target))
      history.DrawContinuous(g, new Rectangle(0, 0, Bins, 3), 0, 0, 1, 0.5);
    Assert.Equal(50, target.GetPixel(237, 0).B);
    Assert.Equal(150, target.GetPixel(237, 1).B);
    Assert.Equal(120, target.GetPixel(237, 2).B);
  }

  [Fact]
  public void SingleSweepSignalSurvivesAreaFilterAndEnergyIsConserved()
  {
    int[][] rows = Enumerable.Range(0, 21).Select(_ => Row(0)).ToArray();
    rows[5][237] = unchecked((int)0xff0000ff);
    var output = new int[10 * Bins];
    IcomScopeContinuousRaster.Render(rows, 0, 0, output, 10);
    int energy = Enumerable.Range(0, 10).Sum(y => output[y * Bins + 237] & 255);
    Assert.InRange(energy * 2, 254, 256);
    Assert.True(output[2 * Bins + 237] != output[2 * Bins + 236]);
  }
}
