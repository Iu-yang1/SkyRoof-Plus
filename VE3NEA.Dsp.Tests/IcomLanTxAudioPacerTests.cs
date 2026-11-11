using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomLanTxAudioPacerTests
  {
    [Theory]
    [InlineData(48000, 960)]
    [InlineData(32000, 640)]
    [InlineData(16000, 320)]
    [InlineData(8000, 160)]
    public void TwentyMillisecondFramesHaveExactIntegralSampleCount(
      int sampleRate, int samplesPerFrame)
    {
      var buffer = new IcomLanTxAudioPacerBuffer(sampleRate, startupBufferMs: 0);
      for (int i = 0; i < 50; i++)
        buffer.NextFrame().Length.Should().Be(samplesPerFrame);
      buffer.OutputFrames.Should().Be(50);
    }

    [Theory]
    [InlineData(11025)]
    [InlineData(22050)]
    [InlineData(44100)]
    public void FractionalFrameRatesHaveNoOneSecondSampleDrift(int sampleRate)
    {
      var buffer = new IcomLanTxAudioPacerBuffer(sampleRate, startupBufferMs: 0);
      int total = 0;
      for (int i = 0; i < 50; i++)
        total += buffer.NextFrame().Length;
      total.Should().Be(sampleRate);
    }

    [Fact]
    public void BurstyMicrophoneInputIsSmoothedOverMultipleTxTicks()
    {
      var buffer = new IcomLanTxAudioPacerBuffer(48000);
      buffer.NextFrame().Should().OnlyContain(v => v == 0);
      buffer.Push(Enumerable.Repeat(0.25f, 2880).ToArray());
      for (int i = 0; i < 3; i++)
      {
        buffer.NextFrame().Should().OnlyContain(v => v == 0.25f);
      }
      buffer.NextFrame().Should().OnlyContain(v => v == 0);
      buffer.UnderrunSamples.Should().Be(960);
    }

    [Fact]
    public void MissingWasapiCallbacksProduceSilenceNotMissingPcmFrames()
    {
      var buffer = new IcomLanTxAudioPacerBuffer(48000);
      buffer.Push(Enumerable.Repeat(0.3f, 2880).ToArray());
      for (int i = 0; i < 3; i++) buffer.NextFrame();
      for (int i = 0; i < 5; i++)
        buffer.NextFrame().Should().OnlyContain(v => v == 0);
      buffer.UnderrunSamples.Should().Be(4800);
      buffer.OutputFrames.Should().Be(8);
    }

    [Fact]
    public void StartupPrebufferWaitsForThreeFramesWithoutConsumingSamples()
    {
      var buffer = new IcomLanTxAudioPacerBuffer(48000);
      buffer.Push(Enumerable.Repeat(0.5f, 1920).ToArray());
      buffer.NextFrame().Should().OnlyContain(v => v == 0);
      buffer.BufferedSamples.Should().Be(1920);
      buffer.Push(Enumerable.Repeat(0.5f, 960).ToArray());
      buffer.NextFrame().Should().OnlyContain(v => v == 0.5f);
      buffer.BufferedSamples.Should().Be(1920);
      buffer.PrefillFrames.Should().Be(1);
    }

    [Fact]
    public void ExcessivelyBackloggedTxDropsOldSamplesWithoutGrowingUnbounded()
    {
      var buffer = new IcomLanTxAudioPacerBuffer(48000);
      buffer.Push(Enumerable.Repeat(0.1f, 14400).ToArray());
      buffer.BufferedSamples.Should().Be(12000);
      buffer.OverflowSamples.Should().Be(2400);
      buffer.Push(Enumerable.Repeat(0.7f, 12000).ToArray());
      buffer.BufferedSamples.Should().Be(12000);
      buffer.OverflowSamples.Should().Be(14400);
      buffer.NextFrame().Should().OnlyContain(v => v == 0.7f);
    }

    [Fact]
    public void PttReleaseResetsAllBufferedAudioBeforeNextTransmit()
    {
      var buffer = new IcomLanTxAudioPacerBuffer(48000);
      buffer.Push(Enumerable.Repeat(0.8f, 4800).ToArray());
      buffer.NextFrame().Should().OnlyContain(v => v == 0.8f);
      buffer.Reset();
      buffer.BufferedSamples.Should().Be(0);
      buffer.NextFrame().Should().OnlyContain(v => v == 0);
      buffer.Push(Enumerable.Repeat(0.2f, 2880).ToArray());
      buffer.NextFrame().Should().OnlyContain(v => v == 0.2f);
    }
  }
}
