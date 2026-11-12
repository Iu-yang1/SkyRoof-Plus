namespace SkyRoof
{
  /// <summary>
  /// Optional 20-ms, RMS-based speech leveler for the PCM sent to Icom LAN.
  /// Separate from the IC-9700's RF ALC and not suitable for digital modes.
  /// The envelope, held per TX session, avoids independently normalizing
  /// each frame and amplifying silent microphone noise.
  /// </summary>
  internal sealed class IcomLanTxVoiceAgc
  {
    private double gain = 1;
    internal double GainDb => 20 * Math.Log10(gain);

    internal void Reset() => gain = 1;

    internal void Process(Span<float> audio, IcomLanTxAgcSettings settings)
    {
      if (!settings.Enabled || audio.IsEmpty)
        return;

      double energy = 0;
      double peak = 0;
      foreach (float value in audio)
      {
        energy += value * (double)value;
        peak = Math.Max(peak, Math.Abs((double)value));
      }

      double rms = Math.Sqrt(energy / audio.Length);
      double target = Math.Clamp(settings.TargetRmsDbfs, -36, -6);
      double maximumGain = Math.Clamp(settings.MaximumGainDb, 0, 24);
      double noiseFloor = Math.Clamp(settings.NoiseFloorDbfs, -90, -24);
      double ceiling = Math.Pow(10, Math.Clamp(settings.PeakCeilingDbfs, -12, -0.1) / 20);
      double rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-9));

      // Sub-threshold audio is not promoted to the voice target.
      double desiredDb = rmsDb < noiseFloor
        ? 0
        : Math.Clamp(target - rmsDb, -30, maximumGain);
      double desired = Math.Pow(10, desiredDb / 20);

      // Peak constraint is instantaneous, including the start of each frame;
      // it protects the PCM stream without relying on later radio-side ALC.
      double peakGain = peak > 0 ? ceiling / peak : double.PositiveInfinity;
      desired = Math.Min(desired, peakGain);
      double start = Math.Min(gain, peakGain);

      bool reducing = desired < start;
      double tauMs = reducing
        ? Math.Clamp(settings.AttackMs, 5, 300)
        : Math.Clamp(settings.ReleaseMs, 50, 3000);
      double follow = 1 - Math.Exp(-20 / tauMs);
      double end = start + (desired - start) * follow;

      for (int i = 0; i < audio.Length; i++)
      {
        double interpolated = start + (end - start) * (i + 1) / audio.Length;
        audio[i] = (float)Math.Clamp(audio[i] * interpolated, -ceiling, ceiling);
      }

      gain = end;
    }
  }
}
