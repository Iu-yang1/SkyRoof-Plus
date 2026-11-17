using System;

namespace SkyRoof.IF
{
  /// <summary>
  /// Voice-only FM fade blanker. Holds a fixed 20-ms lookahead so short
  /// envelope fades can be interpolated between their two valid AF samples.
  /// Long outages pass through unchanged; no lost symbol is synthesized.
  /// This runs after the shared clean-AF tap, never on telemetry samples.
  /// </summary>
  internal sealed class IfAfFmFadeBlanker
  {
    internal const int DelaySamples = IfAfDemodulator.Rate / 50;
    private const int RingLength = DelaySamples + 1;
    private readonly double[] ring = new double[RingLength];
    private readonly double envelopeAlpha =
      1 - Math.Exp(-1.0 / (0.100 * IfAfDemodulator.Rate));
    private long index;
    private long runStart;
    private int runLength;
    private bool prolonged;
    private bool pendingBadNext;
    private bool haveEnvelope;
    private double meanEnvelope;
    private bool haveGood;
    private double lastGood;
    internal long InterpolatedSamples { get; private set; }

    internal double Process(double af, double iqPower)
    {
      double envelope = Math.Sqrt(Math.Max(0, iqPower));
      if (!haveEnvelope && envelope > 0)
      {
        meanEnvelope = envelope;
        haveEnvelope = true;
      }
      bool faded = haveEnvelope && meanEnvelope > 1e-9 &&
        envelope < 0.5 * meanEnvelope;
      meanEnvelope += envelopeAlpha * (envelope - meanEnvelope);
      bool bad = faded || pendingBadNext;
      pendingBadNext = faded;

      ring[(int)(index % RingLength)] = af;
      if (bad)
      {
        if (runLength == 0)
        {
          runStart = index;
          prolonged = false;
        }
        if (runLength <= DelaySamples) runLength++;
        if (runLength > DelaySamples) prolonged = true;
      }
      else
      {
        if (runLength > 0 && !prolonged)
        {
          double left = haveGood ? lastGood : af;
          for (int j = 0; j < runLength; j++)
          {
            ring[(int)((runStart + j) % RingLength)] =
              left + (af - left) * (j + 1.0) / (runLength + 1.0);
          }
          InterpolatedSamples += runLength;
        }
        runLength = 0;
        lastGood = af;
        haveGood = true;
      }

      double result = index >= DelaySamples
        ? ring[(int)((index - DelaySamples) % RingLength)] : 0;
      index++;
      return result;
    }

    internal void Reset()
    {
      Array.Clear(ring);
      index = runStart = 0;
      runLength = 0;
      prolonged = pendingBadNext = haveEnvelope = haveGood = false;
      meanEnvelope = lastGood = 0;
      InterpolatedSamples = 0;
    }
  }

  /// <summary>Second-order Butterworth FM voice high-pass, 250 Hz at 48 kHz.</summary>
  internal sealed class IfAfFmVoiceHighpass
  {
    private readonly double b0, b1, b2, a1, a2;
    private double x1, x2, y1, y2;

    internal IfAfFmVoiceHighpass(double cutoffHz = 250)
    {
      double w = 2 * Math.PI * cutoffHz / IfAfDemodulator.Rate;
      double c = Math.Cos(w);
      double a = Math.Sin(w) / Math.Sqrt(2);
      double divisor = 1 + a;
      b0 = (1 + c) / (2 * divisor);
      b1 = -(1 + c) / divisor;
      b2 = b0;
      a1 = -2 * c / divisor;
      a2 = (1 - a) / divisor;
    }

    internal double Process(double x)
    {
      double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
      x2 = x1; x1 = x; y2 = y1; y1 = y;
      return y;
    }

    internal void Reset() => x1 = x2 = y1 = y2 = 0;
  }

  /// <summary>
  /// FM noise quieting detector. Evaluates the wideband discriminator BEFORE
  /// the 3-kHz voice LPF; higher noise means worse FM carrier quieting.
  /// Thresholds are in dBFS of deviation-normalized discriminator samples,
  /// not IF input power and not calibrated S-meter units.
  /// </summary>
  internal sealed class IfAfFmNoiseQuieting
  {
    private readonly IfAfFmVoiceHighpass highpass = new(3500);
    private readonly double alpha =
      1 - Math.Exp(-1.0 / (0.040 * IfAfDemodulator.Rate));
    private double power;

    internal double Process(double discriminator)
    {
      double high = highpass.Process(discriminator);
      power += alpha * (high * high - power);
      return 10 * Math.Log10(Math.Max(power, 1e-15));
    }

    internal void Reset()
    {
      highpass.Reset();
      power = 0;
    }
  }
}
