using FluentAssertions;
using SkyRoof.CW;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class CwDisplaySessionTests
  {
    [Fact]
    public async Task DisplaySession_PreservesSpectrumBoundsAndOneInFlightFrame()
    {
      using var session = new CwDisplaySession(
        sampleRate: 48000,
        minimumFrequencyHz: 100,
        maximumFrequencyHz: 2000);
      session.MinimumFrequencyHz.Should().Be(100);
      session.MaximumFrequencyHz.Should().Be(2000);
      session.RequiredAudioSeconds.Should().BeApproximately(
        Math.Max(0.20, 8192d / 48000 + 0.02), 1e-12);

      const long generation = 27;
      float[] samples = new float[10000];
      for (int i = 0; i < samples.Length; i++)
        samples[i] = (float)(0.05 * Math.Sin(2 * Math.PI * 730 * i / 48000.0));
      var snapshot = new CwAudioSnapshot(48000, DateTime.UtcNow,
        samples.Length, samples);

      session.TryQueue(snapshot, generation, CwDenoiseMode.Bypass)
        .Should().BeTrue();
      session.TryQueue(snapshot, generation, CwDenoiseMode.Bypass)
        .Should().BeFalse("the display session accepts only one pending FFT job");

      CwDisplayFrameResult result = default;
      bool ready = false;
      for (int retry = 0; retry < 500; retry++)
      {
        if (session.TryTake(out result))
        {
          ready = true;
          break;
        }
        await Task.Delay(10);
      }
      ready.Should().BeTrue("the background FFT should complete without a UI timer");
      result.TimelineGeneration.Should().Be(generation);
      result.DenoiseMode.Should().Be(CwDenoiseMode.Bypass);
      result.Spectrum.PowerDb.Should().HaveCount(512);
      result.Waterfall.PowerDb.Should().HaveCount(512);
      result.Waterfall.MinFrequencyHz.Should().Be(100);
      result.Waterfall.MaxFrequencyHz.Should().Be(2000);
    }
  }
}
