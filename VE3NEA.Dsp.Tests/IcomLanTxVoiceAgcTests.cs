using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomLanTxVoiceAgcTests
  {
    private static IcomLanTxAgcSettings Enabled() => new()
    {
      Enabled = true,
      TargetRmsDbfs = -18,
      MaximumGainDb = 12,
      NoiseFloorDbfs = -48,
      AttackMs = 40,
      ReleaseMs = 450,
      PeakCeilingDbfs = -3
    };

    [Fact]
    public void VoiceAgcIsOptInAndDisabledModeLeavesPcmIdentical()
    {
      var agc = new IcomLanTxVoiceAgc();
      var settings = new IcomLanTxAgcSettings();
      settings.Enabled.Should().BeFalse();

      float[] original = [0.01f, -0.3f, 0.75f, 0, -0.9f];
      float[] actual = original.ToArray();
      agc.Process(actual, settings);
      actual.Should().Equal(original);
      agc.GainDb.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void ConstantQuietVoiceIncreasesLevelWithoutExceedingMaximumGain()
    {
      var agc = new IcomLanTxVoiceAgc();
      var settings = Enabled();
      float[] frame = [];
      for (int n = 0; n < 200; n++)
      {
        frame = Enumerable.Repeat(0.02f, 960).ToArray();
        agc.Process(frame, settings);
      }
      double gain = frame[0] / 0.02;
      gain.Should().BeGreaterThan(2);
      gain.Should().BeLessThanOrEqualTo(Math.Pow(10, 12.0 / 20) + 1e-3);
      agc.GainDb.Should().BeLessThanOrEqualTo(12.01);
    }

    [Fact]
    public void SuddenStrongVoiceNeverExceedsConfiguredPeakCeiling()
    {
      var agc = new IcomLanTxVoiceAgc();
      var settings = Enabled();
      for (int n = 0; n < 100; n++)
      {
        float[] weak = Enumerable.Repeat(0.02f, 960).ToArray();
        agc.Process(weak, settings);
      }

      float[] loud = Enumerable.Repeat(0.98f, 960).ToArray();
      agc.Process(loud, settings);
      double ceiling = Math.Pow(10, settings.PeakCeilingDbfs / 20);
      loud.Max().Should().BeLessThanOrEqualTo((float)(ceiling + 1e-5));
      agc.GainDb.Should().BeLessThan(0);
    }

    [Fact]
    public void BelowNoiseFloorIsNotAmplifiedUpToSpeechTarget()
    {
      var agc = new IcomLanTxVoiceAgc();
      var settings = Enabled();
      for (int n = 0; n < 120; n++)
      {
        float[] quietSpeech = Enumerable.Repeat(0.02f, 960).ToArray();
        agc.Process(quietSpeech, settings);
      }
      for (int n = 0; n < 100; n++)
      {
        float[] noise = Enumerable.Repeat(0.0001f, 960).ToArray();
        agc.Process(noise, settings);
        if (n == 99)
          noise[0].Should().BeApproximately(0.0001f, 0.000005f);
      }
      agc.GainDb.Should().BeApproximately(0, 0.05);
    }

    [Fact]
    public void ResetRestoresUnityGainBetweenPttSessions()
    {
      var agc = new IcomLanTxVoiceAgc();
      var settings = Enabled();
      for (int n = 0; n < 100; n++)
      {
        float[] weak = Enumerable.Repeat(0.02f, 960).ToArray();
        agc.Process(weak, settings);
      }
      agc.GainDb.Should().BeGreaterThan(0);
      agc.Reset();
      agc.GainDb.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void SilenceStaysSilentAndDoesNotGenerateInvalidSamples()
    {
      var agc = new IcomLanTxVoiceAgc();
      var settings = Enabled();
      for (int n = 0; n < 100; n++)
      {
        float[] silent = new float[960];
        agc.Process(silent, settings);
        silent.Should().OnlyContain(x => x == 0 && float.IsFinite(x));
      }
      agc.GainDb.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void BufferDefaultsRetainIndependentNetworkAndMicControls()
    {
      var tx = new IcomDirectLanTransmitSettings();
      tx.PrebufferMs.Should().Be(150);
      tx.CaptureBufferMs.Should().Be(150);
      tx.JitterPrefillMs.Should().Be(60);
      tx.JitterMaximumMs.Should().Be(250);
      tx.MicrophoneGain.Should().Be(100);
      tx.Agc.Enabled.Should().BeFalse();
      new IcomDirectLanReceiveSettings().PrebufferMs.Should().Be(120);
    }
  }
}
