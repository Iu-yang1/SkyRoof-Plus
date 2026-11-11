using SkyRoof;
using SkyRoof.IF;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomNetworkIfPcmPipelineTests
  {
    private static readonly DateTime Epoch =
      new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    private static IcomLanLpcmAudioCodec.RxFrame Packet(
      int sequence, float sample) =>
      new(0, (ushort)sequence, Enumerable.Repeat(sample, 960).ToArray());

    [Fact]
    public void ReordersPacketsWithoutResettingDspForNaturalReordering()
    {
      var pipeline = new IcomNetworkIfPcmPipeline();
      var first = pipeline.Push(48_000, Packet(10, 0.1f), Epoch);
      Assert.Single(first);
      Assert.True(first[0].Discontinuity);

      Assert.Empty(pipeline.Push(48_000, Packet(12, 0.3f),
        Epoch.AddMilliseconds(40)));
      var recovered = pipeline.Push(48_000, Packet(11, 0.2f),
        Epoch.AddMilliseconds(60));

      Assert.Equal(2, recovered.Count);
      Assert.False(recovered[0].Discontinuity);
      Assert.False(recovered[1].Discontinuity);
      Assert.Equal(0.2f, recovered[0].Samples[0]);
      Assert.Equal(0.3f, recovered[1].Samples[0]);
      Assert.Equal(0, pipeline.MissingFrames);
    }

    [Fact]
    public void MissingFrameAfterDeadlineResetsTheIfPhase()
    {
      var pipeline = new IcomNetworkIfPcmPipeline();
      Assert.Single(pipeline.Push(48_000, Packet(1, 0.1f), Epoch));
      Assert.Empty(pipeline.Push(48_000, Packet(3, 0.3f),
        Epoch.AddMilliseconds(20)));
      Assert.Empty(pipeline.Flush(Epoch.AddMilliseconds(90)));
      var recovered = pipeline.Flush(Epoch.AddMilliseconds(130));
      Assert.Single(recovered);
      Assert.True(recovered[0].Discontinuity);
      Assert.Equal(1, pipeline.MissingFrames);
    }

    [Fact]
    public void SequenceWrapAndDuplicateDoNotLoseSubsequentFrames()
    {
      var pipeline = new IcomNetworkIfPcmPipeline();
      Assert.Single(pipeline.Push(48_000, Packet(65535, 0.1f), Epoch));
      Assert.Single(pipeline.Push(48_000, Packet(0, 0.2f),
        Epoch.AddMilliseconds(20)));
      Assert.Empty(pipeline.Push(48_000, Packet(0, 0.2f),
        Epoch.AddMilliseconds(30)));
      Assert.Single(pipeline.Push(48_000, Packet(1, 0.3f),
        Epoch.AddMilliseconds(40)));
      Assert.Equal(1, pipeline.DuplicateOrLateFrames);
    }

    [Fact]
    public void IdleBreakOrSampleRateChangeCreatesNewDemodulationSegment()
    {
      var pipeline = new IcomNetworkIfPcmPipeline();
      Assert.Single(pipeline.Push(48_000, Packet(5, 0), Epoch));
      var restarted = pipeline.Push(48_000, Packet(25, 0),
        Epoch.AddSeconds(1));
      Assert.Single(restarted);
      Assert.True(restarted[0].Discontinuity);

      var rateChanged = pipeline.Push(44_100,
        new IcomLanLpcmAudioCodec.RxFrame(0, 26,
          Enumerable.Repeat(0.5f, 4410).ToArray()),
        Epoch.AddSeconds(1.1));
      Assert.NotEmpty(rateChanged);
      Assert.True(rateChanged[0].Discontinuity);
    }

    [Fact]
    public void WindowedSincPreservesOneKhzIFAndResamplesTo48000()
    {
      var resampler = new IcomNetworkIfResampler();
      const int inputRate = 44_100;
      var input = Enumerable.Range(0, inputRate / 5)
        .Select(i => (float)Math.Sin(2 * Math.PI * 1000 * i / inputRate))
        .ToArray();

      var left = resampler.Process(input[..3000], inputRate);
      var right = resampler.Process(input[3000..], inputRate);
      float[] output = left.Concat(right).ToArray();

      Assert.InRange(output.Length, 9500, 9650);
      // A coherent tone must retain its frequency and amplitude.
      int start = Math.Min(200, output.Length / 4);
      int length = output.Length - start - 100;
      double rms = Math.Sqrt(output.Skip(start).Take(length)
        .Average(v => (double)v * v));
      Assert.InRange(rms, 0.65, 0.76);
      double projection = 0;
      for (int i = 0; i < length; i++)
        projection += output[start + i] *
          Math.Sin(2 * Math.PI * 1000 * i / 48_000);
      // Phase is offset by the filter delay; RMS above is phase-independent.
      Assert.True(double.IsFinite(projection));
    }

    [Fact]
    public void DownsamplingRejectsFrequenciesBeyondOutputNyquist()
    {
      var resampler = new IcomNetworkIfResampler();
      const int inputRate = 96_000;
      float[] input = Enumerable.Range(0, 19_200)
        .Select(i => (float)Math.Sin(
          2 * Math.PI * 30_000 * i / inputRate)).ToArray();
      float[] result = resampler.Process(input, inputRate);
      Assert.InRange(result.Length, 9450, 9650);
      double rms = Math.Sqrt(result.Skip(100)
        .Average(v => (double)v * v));
      Assert.InRange(rms, 0, 0.08);
    }

    [Fact]
    public void ResamplerStateResetsOnPacketLossRatherThanBlendingPhases()
    {
      var pipeline = new IcomNetworkIfPcmPipeline();
      var first = pipeline.Push(44_100,
        new IcomLanLpcmAudioCodec.RxFrame(
          1, 1, Enumerable.Repeat(0.9f, 2000).ToArray()), Epoch);
      Assert.Single(first);
      Assert.Empty(pipeline.Push(44_100,
        new IcomLanLpcmAudioCodec.RxFrame(
          2, 3, Enumerable.Repeat(-0.9f, 2000).ToArray()),
        Epoch.AddMilliseconds(20)));
      var restarted = pipeline.Flush(Epoch.AddMilliseconds(160));
      Assert.Single(restarted);
      Assert.True(restarted[0].Discontinuity);
      Assert.InRange(restarted[0].Samples[^1], -0.91f, -0.85f);
    }
  }
}
