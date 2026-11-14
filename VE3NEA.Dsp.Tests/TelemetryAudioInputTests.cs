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
