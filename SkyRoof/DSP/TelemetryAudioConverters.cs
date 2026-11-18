using System;
using System.Linq;
using MathNet.Numerics;

namespace SkyRoof
{
  /// <summary>
  /// Converts 48-kHz REAL AF or a REAL 12-kHz IF into positive-frequency
  /// analytic IQ, then frequency-translates its selected carrier to zero.
  /// A centered Hilbert FIR and a continuous NCO preserve phase across
  /// arbitrary WASAPI block boundaries. Raw IF is not FM/SSB-demodulated:
  /// the existing SkySignals IQ modem performs the final modem demodulation.
  /// </summary>
  public sealed class TelemetryRealAudioIqConverter
  {
    private const int Rate = 48000;
    private const int Taps = 129;
    private const int Delay = (Taps - 1) / 2;
    private readonly float[] history = new float[Taps];
    private readonly double[] hilbert = new double[Taps];
    private readonly double stepCos, stepSin;
    private int next;
    private double oscillatorCos = 1, oscillatorSin;
    private int oscillatorSamples;

    public int CenterFrequencyHz { get; }

    public TelemetryRealAudioIqConverter(int centerFrequencyHz)
    {
      if (centerFrequencyHz <= 0 || centerFrequencyHz >= Rate / 2)
        throw new ArgumentOutOfRangeException(nameof(centerFrequencyHz));
      CenterFrequencyHz = centerFrequencyHz;
      double omega = 2.0 * Math.PI * centerFrequencyHz / Rate;
      stepCos = Math.Cos(omega);
      stepSin = Math.Sin(omega);
      foreach (int i in Enumerable.Range(0, Taps))
      {
        int lag = i - Delay;
        if (lag == 0 || (lag & 1) == 0) continue;
        double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (Taps - 1));
        // Positive-frequency analytic representation: cos(wt) + j sin(wt).
        hilbert[i] = 2.0 * window / (Math.PI * lag);
      }
    }

    public Complex32[] Convert(ReadOnlySpan<float> input)
    {
      var result = new Complex32[input.Length];
      int i = 0;
      foreach (float sample in input)
      {
        history[next] = float.IsFinite(sample) ? sample : 0;
        int delayed = next - Delay;
        if (delayed < 0) delayed += Taps;
        double real = history[delayed];
        double imaginary = 0;
        // The centred 129-tap Hilbert FIR has non-zero coefficients ONLY
        // at odd indices: skip 65 zero terms per output sample.
        int read = next - 1;
        if (read < 0) read += Taps;
        for (int k = 1; k < Taps; k += 2)
        {
          imaginary += hilbert[k] * history[read];
          read -= 2;
          if (read < 0) read += Taps;
        }
        if (++next == Taps) next = 0;
        double c = oscillatorCos, sin = oscillatorSin;
        result[i++] = new Complex32(
          (float)(real * c + imaginary * sin),
          (float)(imaginary * c - real * sin));
        // One complex multiply per sample replaces 96k sin/cos calls/s.
        // A periodic unit-norm correction bounds long-recording drift.
        oscillatorCos = c * stepCos - sin * stepSin;
        oscillatorSin = sin * stepCos + c * stepSin;
        if ((++oscillatorSamples & 4095) == 0)
        {
          double norm = Math.Sqrt(oscillatorCos * oscillatorCos +
            oscillatorSin * oscillatorSin);
          oscillatorCos /= norm;
          oscillatorSin /= norm;
        }
      }
      return result;
    }

    public void Reset()
    {
      Array.Clear(history);
      next = 0;
      oscillatorCos = 1;
      oscillatorSin = 0;
      oscillatorSamples = 0;
    }
  }

  /// <summary>
  /// Compatibility bridge for already FM-discriminated AFSK audio.
  /// The vendored SkyTlm AFSK modem expects IQ of an FM carrier and applies
  /// its own FM discriminator. Frequency-modulating an ideal analytic carrier
  /// with the existing AF waveform makes the discriminator recover that AF
  /// once, instead of erroneously differentiating its Hilbert representation.
  /// </summary>
  public sealed class TelemetryAfskFmIqConverter
  {
    private const double Rate = 48000.0;
    private readonly double maxDeviationHz;
    private double phase;

    public TelemetryAfskFmIqConverter(double maxDeviationHz = 1800.0)
    {
      this.maxDeviationHz = maxDeviationHz;
    }

    public Complex32[] Convert(ReadOnlySpan<float> audio)
    {
      var result = new Complex32[audio.Length];
      int index = 0;
      foreach (float value in audio)
      {
        double sample = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0;
        phase += 2 * Math.PI * maxDeviationHz * sample / Rate;
        if (phase > Math.PI) phase -= 2 * Math.PI;
        if (phase < -Math.PI) phase += 2 * Math.PI;
        result[index++] = new Complex32((float)Math.Cos(phase), (float)Math.Sin(phase));
      }
      return result;
    }

    public void Reset() => phase = 0;
  }

}
