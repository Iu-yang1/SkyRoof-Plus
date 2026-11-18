using System;
using MathNet.Numerics;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class TelemetryAudioInputTests
  {
    [Theory]
    [InlineData(12000, 12000)]
    [InlineData(1700, 1700)]
    [InlineData(1700, 2200)]
    [InlineData(12000, 13500)]
    [InlineData(12000, 10500)]
    // Measured 2026-11-18 IQ: CFO around -817 Hz, +/-820 Hz FSK
    // peak deviation. The higher tone lies almost on the IQ zero frequency.
    [InlineData(12000, 10363)]
    [InlineData(12000, 12003)]
    // Earlier observed -1211 Hz carrier displacement plus +/-820 Hz:
    // both tones remain available after the 12-kHz real IF analytic bridge.
    [InlineData(12000, 9969)]
    [InlineData(12000, 11609)]
    [InlineData(1700, 1200)]
    public void RealIfAndAf_AreTranslatedToContinuousComplexBaseband(
      int centerHz, int testHz)
    {
      const int sampleRate = 48000;
      const int total = 9600;
      var adapter = new TelemetryRealAudioIqConverter(centerHz);
      var converted = new Complex32[total];
      // Exercise chunk boundaries that do not align with 12-kHz 4-sample NCO.
      int[] sizes = { 1600, 333, 267, 2000, 500, 4900 };
      int offset = 0;
      foreach (int size in sizes)
      {
        var pcm = new float[size];
        for (int i = 0; i < size; i++)
          pcm[i] = (float)(0.6 * Math.Cos(
            2 * Math.PI * testHz * (offset + i) / sampleRate));
        Complex32[] iq = adapter.Convert(pcm);
        Array.Copy(iq, 0, converted, offset, iq.Length);
        offset += size;
      }

      Assert.Equal(total, offset);
      double sumReal = 0, sumImag = 0, sumPower = 0;
      const int first = 400;
      for (int i = first + 1; i < total; i++)
      {
        double r0 = converted[i - 1].Real, q0 = converted[i - 1].Imaginary;
        double r1 = converted[i].Real, q1 = converted[i].Imaginary;
        sumReal += r1 * r0 + q1 * q0;
        sumImag += q1 * r0 - r1 * q0;
        sumPower += r1 * r1 + q1 * q1;
      }
      double measured = Math.Atan2(sumImag, sumReal) *
        sampleRate / (2 * Math.PI);
      Assert.InRange(measured, testHz - centerHz - 8.0,
        testHz - centerHz + 8.0);
      double rms = Math.Sqrt(sumPower / (total - first - 1));
      Assert.InRange(rms, 0.4, 0.8);
    }

    [Fact]
    public void AfskAudioBridge_RestoresAudioAfterFmDiscrimination()
    {
      const int rate = 48000;
      const int total = 4800;
      var samples = new float[total];
      for (int i = 0; i < total; i++)
        samples[i] = (float)(0.35 * Math.Sin(2 * Math.PI * 1200 * i / rate));
      var bridge = new TelemetryAfskFmIqConverter();
      var iq = bridge.Convert(samples.AsSpan(0, 1333));
      var tail = bridge.Convert(samples.AsSpan(1333));
      var all = new Complex32[total];
      iq.CopyTo(all, 0);
      tail.CopyTo(all, iq.Length);
      double mse = 0;
      for (int i = 1; i < total; i++)
      {
        double re = all[i].Real * all[i - 1].Real +
          all[i].Imaginary * all[i - 1].Imaginary;
        double im = all[i].Imaginary * all[i - 1].Real -
          all[i].Real * all[i - 1].Imaginary;
        double recovered = Math.Atan2(im, re) * rate / (2 * Math.PI * 1800);
        mse += Math.Pow(recovered - samples[i], 2);
      }
      Assert.InRange(Math.Sqrt(mse / (total - 1)), 0, 1e-5);
      bridge.Reset();
      var zero = bridge.Convert(new float[16]);
      Assert.All(zero, s => Assert.InRange(s.Real, 0.99999f, 1.00001f));
    }

    [Fact]
    public void RecordedIfWavAndLiveIfChunksProduceEquivalentIq()
    {
      const int total = 48000;
      var raw = new float[total];
      for (int i = 0; i < total; i++)
      {
        double t = i / 48000.0;
        raw[i] = (float)(0.4 * Math.Sin(2 * Math.PI * 12000 * t +
          0.5 * Math.Sin(2 * Math.PI * 200 * t)));
      }
      var fileReplay = new TelemetryRealAudioIqConverter(12000).Convert(raw);
      var live = new TelemetryRealAudioIqConverter(12000);
      for (int offset = 0; offset < total; offset += 1600)
      {
        var chunk = live.Convert(raw.AsSpan(offset, Math.Min(1600, total - offset)));
        for (int j = 0; j < chunk.Length; j++)
        {
          Assert.InRange(Math.Abs(chunk[j].Real - fileReplay[offset + j].Real), 0, 1e-6);
          Assert.InRange(Math.Abs(chunk[j].Imaginary - fileReplay[offset + j].Imaginary), 0, 1e-6);
        }
      }
    }

    [Fact]
    public void ResetStartsFreshCarrierPhaseAndClearsHilbertHistory()
    {
      var decoder = new TelemetryRealAudioIqConverter(12000);
      var audio = new float[400];
      for (int i = 0; i < audio.Length; i++)
        audio[i] = (float)Math.Sin(2 * Math.PI * 13200 * i / 48000);
      decoder.Convert(audio);
      decoder.Reset();
      Complex32[] output = decoder.Convert(new float[512]);
      foreach (Complex32 sample in output)
      {
        Assert.Equal(0, sample.Real, precision: 7);
        Assert.Equal(0, sample.Imaginary, precision: 7);
      }
    }

    [Fact]
    public void TelemetryDefaultsToPriorSdrIqRoute()
    {
      var settings = new TelemetrySettings();
      Assert.Equal(TelemetryReceiveSource.SdrIq, settings.InputSource);
      Assert.Equal(1700, settings.AfCenterHz);
      Assert.Equal(RecordingAudioSource.Auto, new AudioSettings().RecorderSource);
    }
  }
}
