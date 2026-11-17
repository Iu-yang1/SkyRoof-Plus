using System;
using System.Diagnostics;
using Newtonsoft.Json;
using SkyRoof.IF;
using Xunit;
using Xunit.Abstractions;

namespace VE3NEA.Dsp.Tests
{
  /// <summary>Deterministic 12-kHz real-IF FM tests. Not an IC-9700 RF sensitivity measurement.</summary>
  public sealed class IfAfFmVoicePipelineTests
  {
    private const int Rate = IfAfDemodulator.Rate;
    private readonly ITestOutputHelper output;
    public IfAfFmVoicePipelineTests(ITestOutputHelper output) => this.output = output;

    private static float[] FmIf(double afHz, int count = Rate, double deviationHz = 1200)
    {
      float[] pcm = new float[count];
      double phase = 0;
      for (int n = 0; n < pcm.Length; n++)
      {
        double freq = 12000 + deviationHz *
          Math.Sin(2 * Math.PI * afHz * n / Rate);
        phase += 2 * Math.PI * freq / Rate;
        pcm[n] = (float)(0.45 * Math.Cos(phase));
      }
      return pcm;
    }

    private static double Strength(ReadOnlySpan<float> af, double hz)
    {
      double re = 0, im = 0;
      int start = af.Length / 2;
      for (int n = start; n < af.Length; n++)
      {
        double a = 2 * Math.PI * hz * n / Rate;
        re += af[n] * Math.Cos(a);
        im += af[n] * Math.Sin(a);
      }
      return 2 * Math.Sqrt(re * re + im * im) / (af.Length - start);
    }

    [Fact]
    public void CleanTelemetryAfIsIdenticalWithAllVoiceEnhancementsEnabled()
    {
      var input = FmIf(900);
      var reference = new IfAfDemodulator(IfAfMode.Fm);
      var enhanced = new IfAfDemodulator(IfAfMode.Fm,
        fmVoice: IfAfFmVoiceProcessing.Voice750us,
        fmSquelch: true, fmSquelchDbfs: -45,
        fmVoiceHighpass: true, fmFadeBlanker: true,
        fmSquelchSource: IfAfFmSquelchSource.NoiseQuieting,
        fmNoiseOpenDbfs: -30);
      var refAf = new float[input.Length];
      var cleanAf = new float[input.Length];
      var speechAf = new float[input.Length];
      reference.Process(input, refAf);
      enhanced.Process(input, speechAf, cleanAf);
      Assert.Equal(refAf, cleanAf);
      Assert.True(Strength(refAf, 900) > 0.15);
      Assert.True(Strength(speechAf, 900) < Strength(refAf, 900) * .6);
    }

    [Fact]
    public void Voice750usDeemphasisPreservesPersistedEnumIdsAndRollsOffSpeech()
    {
      Assert.Equal(0, (int)IfAfFmVoiceProcessing.Off);
      Assert.Equal(1, (int)IfAfFmVoiceProcessing.Voice50us);
      Assert.Equal(2, (int)IfAfFmVoiceProcessing.Voice75us);
      Assert.Equal(3, (int)IfAfFmVoiceProcessing.Voice750us);
      double Raw(double hz, IfAfFmVoiceProcessing profile)
      {
        var source = FmIf(hz);
        var af = new float[source.Length];
        new IfAfDemodulator(IfAfMode.Fm, fmVoice: profile).Process(source, af);
        return Strength(af, hz);
      }
      double lowRatio = Raw(400, IfAfFmVoiceProcessing.Voice750us) /
        Raw(400, IfAfFmVoiceProcessing.Off);
      double highRatio = Raw(2700, IfAfFmVoiceProcessing.Voice750us) /
        Raw(2700, IfAfFmVoiceProcessing.Off);
      Assert.True(lowRatio > highRatio * 2,
        $"750us de-emphasis should attenuate 2700 Hz more than 400 Hz: {lowRatio}/{highRatio}");
    }

    [Fact]
    public void OptionalVoiceHighpassRejects67HzCtcssButNot900HzSpeech()
    {
      double Response(double hz, bool highpass)
      {
        float[] source = FmIf(hz);
        float[] af = new float[source.Length];
        new IfAfDemodulator(IfAfMode.Fm, fmVoiceHighpass: highpass)
          .Process(source, af);
        return Strength(af, hz);
      }
      double ctcss = Response(67, true) / Response(67, false);
      double speech = Response(900, true) / Response(900, false);
      Assert.True(ctcss < .25, $"67-Hz reduction insufficient: {ctcss}");
      Assert.InRange(speech, .90, 1.05);
    }

    [Fact]
    public void FadeBlankerIsExactly20msDelayedForCleanCarrier()
    {
      var blanker = new IfAfFmFadeBlanker();
      for (int n = 0; n < 2600; n++)
      {
        double value = Math.Sin(2 * Math.PI * 900 * n / Rate);
        double got = blanker.Process(value, 1);
        double expected = n >= IfAfFmFadeBlanker.DelaySamples
          ? Math.Sin(2 * Math.PI * 900 *
              (n - IfAfFmFadeBlanker.DelaySamples) / Rate) : 0;
        Assert.InRange(Math.Abs(got - expected), 0, 1e-12);
      }
      Assert.Equal(0, blanker.InterpolatedSamples);
    }

    [Fact]
    public void FadeBlankerInterpolatesShortOutageButLeavesLongOutageUninvented()
    {
      var shortFade = new IfAfFmFadeBlanker();
      var longFade = new IfAfFmFadeBlanker();
      double shortPeak = 0, longPeak = 0;
      for (int n = 0; n < 4300; n++)
      {
        bool shortBad = n >= 1200 && n < 1500;
        bool longBad = n >= 1200 && n < 2700;
        double good = .15 * Math.Sin(2 * Math.PI * 500 * n / Rate);
        double shortOut = shortFade.Process(shortBad ? 4 : good, shortBad ? 0 : 1);
        double longOut = longFade.Process(longBad ? 4 : good, longBad ? 0 : 1);
        if (n >= 1200 + IfAfFmFadeBlanker.DelaySamples &&
            n < 1500 + IfAfFmFadeBlanker.DelaySamples)
          shortPeak = Math.Max(shortPeak, Math.Abs(shortOut));
        if (n >= 2000 + IfAfFmFadeBlanker.DelaySamples &&
            n < 2600 + IfAfFmFadeBlanker.DelaySamples)
          longPeak = Math.Max(longPeak, Math.Abs(longOut));
      }
      Assert.True(shortFade.InterpolatedSamples >= 300);
      Assert.True(shortPeak < .3, $"short fade impulse not interpolated: {shortPeak}");
      Assert.True(longPeak > 3, $"long outage must pass through: {longPeak}");
    }

    [Fact]
    public void QuietingDetectorMeasuresDiscriminatorNoiseAboveVoiceBand()
    {
      var quiet = new IfAfFmNoiseQuieting();
      var noisy = new IfAfFmNoiseQuieting();
      double low = 0, high = 0;
      for (int n = 0; n < Rate; n++)
      {
        low = quiet.Process(.2 * Math.Sin(2 * Math.PI * 800 * n / Rate));
        high = noisy.Process(.2 * Math.Sin(2 * Math.PI * 9000 * n / Rate));
      }
      Assert.True(high > low * 20, $"noise quieting detector lacks separation: {high}/{low}");
    }

    [Fact]
    public void FmVoiceDspStaysSampleIdenticalAcrossDifferentBufferPartitions()
    {
      float[] input = FmIf(900);
      float[] single = new float[input.Length];
      float[] chunks = new float[input.Length];
      var a = new IfAfDemodulator(IfAfMode.Fm, fmFadeBlanker: true,
        fmVoiceHighpass: true, fmVoice: IfAfFmVoiceProcessing.Voice750us);
      var b = new IfAfDemodulator(IfAfMode.Fm, fmFadeBlanker: true,
        fmVoiceHighpass: true, fmVoice: IfAfFmVoiceProcessing.Voice750us);
      a.Process(input, single);
      for (int at = 0; at < input.Length; at += 317)
      {
        int n = Math.Min(317, input.Length - at);
        b.Process(input.AsSpan(at, n), chunks.AsSpan(at, n));
      }
      Assert.Equal(single, chunks);
    }

    [Fact]
    public void NewFmSettingsRoundTripAndDefaultToLegacyVoiceBehavior()
    {
      var defaults = new IfAfSettings();
      Assert.False(defaults.FmFadeBlankerEnabled);
      Assert.False(defaults.FmVoiceHighpassEnabled);
      Assert.False(defaults.FmVoiceSquelchEnabled);
      Assert.Equal(IfAfFmSquelchSource.CarrierPower, defaults.FmSquelchSource);
      var configured = new IfAfSettings
      {
        FmVoiceProcessing = IfAfFmVoiceProcessing.Voice750us,
        FmFadeBlankerEnabled = true,
        FmVoiceHighpassEnabled = true,
        FmSquelchSource = IfAfFmSquelchSource.NoiseQuieting,
        FmVoiceSquelchEnabled = true,
        FmNoiseOpenDbfs = -23
      };
      var restored = JsonConvert.DeserializeObject<IfAfSettings>(
        JsonConvert.SerializeObject(configured));
      Assert.NotNull(restored);
      Assert.Equal(configured.FmVoiceProcessing, restored.FmVoiceProcessing);
      Assert.Equal(configured.FmFadeBlankerEnabled, restored.FmFadeBlankerEnabled);
      Assert.Equal(configured.FmVoiceHighpassEnabled, restored.FmVoiceHighpassEnabled);
      Assert.Equal(configured.FmSquelchSource, restored.FmSquelchSource);
      Assert.Equal(configured.FmNoiseOpenDbfs, restored.FmNoiseOpenDbfs);
    }

    [Fact]
    public void Fm48kStreamingSmokeAndRepeatableThroughputMeasurement()
    {
      float[] input = FmIf(1200);
      var dsp = new IfAfDemodulator(IfAfMode.Fm, fmFadeBlanker: true,
        fmVoiceHighpass: true, fmSquelch: true,
        fmSquelchSource: IfAfFmSquelchSource.NoiseQuieting);
      float[] outputAf = new float[input.Length];
      var watch = Stopwatch.StartNew();
      dsp.Process(input, outputAf);
      watch.Stop();
      Assert.Equal(input.Length, dsp.SamplesProcessed);
      Assert.All(outputAf, x => Assert.True(float.IsFinite(x)));
      output.WriteLine($"FM 48k DSP 1-second synthetic IF: {watch.Elapsed.TotalMilliseconds:F1} ms " +
        "(CI diagnostic, not a cross-CPU performance contract)");
    }
  }
}
