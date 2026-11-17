using System;

namespace SkyRoof.IF
{
  // Group voice-specific tuning without expanding the decoder constructor ABI.
  internal sealed class IfAfFmVoiceOptions
  {
    internal IfAfFmVoiceProcessing Deemphasis { get; init; }
    internal bool SquelchEnabled { get; init; }
    internal int PowerThresholdDbfs { get; init; }
    internal bool HighpassEnabled { get; init; }
    internal bool BlankerEnabled { get; init; }
    internal IfAfFmSquelchSource SquelchSource { get; init; }
    internal int NoiseOpenDbfs { get; init; }
  }

  /// <summary>Optional local voice-only enhancements. Digital USB-D/LSB-D,
  /// CW, AFC and raw IF never pass through this processor.</summary>
  internal sealed class IfAfVoiceEnhancements
  {
    private readonly IfAfFmVoiceProcessing fmVoice;
    private readonly bool fmSquelch;
    private readonly double fmPowerThreshold;
    private readonly double fmNoiseOpenPower;
    private readonly IfAfFmFadeBlanker? fmBlanker;
    private readonly IfAfFmVoiceHighpass? fmHighpass;
    private readonly IfAfFmNoiseQuieting? fmQuieting;
    private readonly double[]? fmGateDelay;
    private int fmGateDelayWrite;
    private readonly IfAfSsbVoiceNotch ssbNotch;
    private readonly IfAfAdaptiveNotch? adaptiveNotch;
    private readonly IfAfSpectralNoiseReducer? noiseReducer;
    private readonly double nb0, nb1, nb2, na1, na2;
    private double nz1, nz2;
    private double fmSmoothPower, fmGate = 1, deemph;
    private IfAfMode? previousMode;

    internal IfAfVoiceEnhancements(IfAfFmVoiceOptions fm,
      IfAfSsbVoiceNotch ssbNotch, IfAfSsbNoiseReduction noiseReduction)
    {
      if (!Enum.IsDefined(typeof(IfAfFmVoiceProcessing), fm.Deemphasis) ||
          !Enum.IsDefined(typeof(IfAfSsbVoiceNotch), ssbNotch) ||
          !Enum.IsDefined(typeof(IfAfSsbNoiseReduction), noiseReduction) ||
          !Enum.IsDefined(typeof(IfAfFmSquelchSource), fm.SquelchSource) ||
          fm.NoiseOpenDbfs is < -90 or > 20 ||
          fm.PowerThresholdDbfs is < -95 or > -10)
        throw new ArgumentOutOfRangeException(nameof(fm));
      fmVoice = fm.Deemphasis;
      fmSquelch = fm.SquelchEnabled;
      fmPowerThreshold = Math.Pow(10, fm.PowerThresholdDbfs / 10.0);
      fmNoiseOpenPower = Math.Pow(10, fm.NoiseOpenDbfs / 10.0);
      if (fm.BlankerEnabled) fmBlanker = new IfAfFmFadeBlanker();
      if (fm.BlankerEnabled && fm.SquelchEnabled)
        fmGateDelay = new double[IfAfFmFadeBlanker.DelaySamples + 1];
      if (fm.HighpassEnabled) fmHighpass = new IfAfFmVoiceHighpass();
      if (fm.SquelchEnabled && fm.SquelchSource == IfAfFmSquelchSource.NoiseQuieting)
        fmQuieting = new IfAfFmNoiseQuieting();
      this.ssbNotch = ssbNotch;
      if (ssbNotch == IfAfSsbVoiceNotch.AutoTracking)
        adaptiveNotch = new IfAfAdaptiveNotch();
      if (noiseReduction == IfAfSsbNoiseReduction.Mild)
        noiseReducer = new IfAfSpectralNoiseReducer();
      int hz = ssbNotch switch
      {
        IfAfSsbVoiceNotch.Tone1000Hz => 1000,
        IfAfSsbVoiceNotch.Tone1500Hz => 1500,
        _ => 0
      };
      if (hz > 0)
      {
        double omega = 2 * Math.PI * hz / IfAfDemodulator.Rate;
        double alpha = Math.Sin(omega) / (2 * (hz / 45.0));
        double a0 = 1 + alpha;
        nb0 = nb2 = 1 / a0;
        nb1 = -2 * Math.Cos(omega) / a0;
        na1 = nb1;
        na2 = (1 - alpha) / a0;
      }
    }

    internal void Reset()
    {
      nz1 = nz2 = deemph = fmSmoothPower = 0;
      fmGate = fmSquelch ? 0 : 1;
      previousMode = null;
      adaptiveNotch?.Reset();
      noiseReducer?.Reset();
      fmBlanker?.Reset();
      fmHighpass?.Reset();
      fmQuieting?.Reset();
      if (fmGateDelay != null) Array.Clear(fmGateDelay);
      fmGateDelayWrite = 0;
    }

    internal double Process(double sample, IfAfMode mode, double fmIqPower = 0,
      double fmWidebandDiscriminator = 0)
    {
      if (mode != previousMode)
      {
        Reset();
        previousMode = mode;
      }
      if (mode is IfAfMode.Usb or IfAfMode.Lsb)
      {
        if (ssbNotch == IfAfSsbVoiceNotch.AutoTracking)
          sample = adaptiveNotch!.Process(sample);
        else if (ssbNotch != IfAfSsbVoiceNotch.Off)
        {
          double y = nb0 * sample + nz1;
          nz1 = nb1 * sample - na1 * y + nz2;
          nz2 = nb2 * sample - na2 * y;
          sample = y;
        }
        return noiseReducer?.Process(sample) ?? sample;
      }
      if (mode != IfAfMode.Fm) return sample;
      // Applied only after the clean discriminator-AF tap. Optional blanking
      // delays voice by 20 ms but cannot change telemetry AF or raw IF.
      if (fmBlanker != null) sample = fmBlanker.Process(sample, fmIqPower);
      if (fmVoice != IfAfFmVoiceProcessing.Off)
      {
        // First-order FM voice de-emphasis. No processing in the data tap.
        double tau = fmVoice switch
        {
          IfAfFmVoiceProcessing.Voice50us => 50e-6,
          IfAfFmVoiceProcessing.Voice75us => 75e-6,
          IfAfFmVoiceProcessing.Voice750us => 750e-6,
          _ => 0
        };
        double alpha = 1 - Math.Exp(-1 / (tau * IfAfDemodulator.Rate));
        deemph += alpha * (sample - deemph);
        sample = deemph;
      }

      if (fmHighpass != null) sample = fmHighpass.Process(sample);

      if (fmSquelch)
      {
        // ~50 ms RF power envelope, with 3 dB close hysteresis and 10 ms
        // audio gate ramp. Sample-exact zero output when fully closed.
        double a = 1 - Math.Exp(-1.0 / (0.05 * IfAfDemodulator.Rate));
        fmSmoothPower += a * (Math.Max(0, fmIqPower) - fmSmoothPower);
        double closePower = fmPowerThreshold / 2;
        bool carrierPresent = fmGate > 0.01
          ? fmSmoothPower >= closePower
          : fmSmoothPower >= fmPowerThreshold;
        double noisePower = fmQuieting?.Process(fmWidebandDiscriminator) ?? 0;
        bool quietEnough = fmQuieting == null ||
          noisePower <= fmNoiseOpenPower * (fmGate > 0.01 ? 1.995262315 : 1);
        bool opening = carrierPresent && quietEnough;
        double target = opening ? 1 : 0;
        double step = 1.0 / (0.010 * IfAfDemodulator.Rate);
        fmGate = Math.Clamp(fmGate + Math.Sign(target - fmGate) * step, 0, 1);
        // The blanker delays audio by 20 ms; delay the squelch gain by
        // exactly the same number of samples to preserve speech onsets.
        double gate = fmGate;
        if (fmGateDelay != null)
        {
          int read = (fmGateDelayWrite + 1) % fmGateDelay.Length;
          fmGateDelay[fmGateDelayWrite] = fmGate;
          gate = fmGateDelay[read];
          fmGateDelayWrite = read;
        }
        sample *= gate;
      }
      return sample;
    }
  }
}
