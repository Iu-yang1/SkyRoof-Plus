using System;
using System.Collections.Generic;

namespace SkyRoof
{
  /// <summary>
  /// English presentation for legacy AFC model metadata that was authored
  /// directly in Chinese. CLR identifiers and persistent settings are unchanged.
  /// </summary>
  internal static class SettingsEnglishSupplement
  {
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
      ["AfcSettings.CaptureChannel"] = "IF capture channel",
      ["AfcSettings.AcquisitionPreset"] = "Acquisition strategy (speed / sensitivity)",
      ["AfcSettings.SignalLossPolicy"] = "AFC correction after signal loss",
      ["AfcSettings.FmReferenceCandidateHz"] = "FM candidate frequency (Hz)"
    };

    private static readonly Dictionary<string, string> Help = new(StringComparer.Ordinal)
    {
      ["AfcSettings.Enabled"] = "Enable automatic frequency control for the receive path.",
      ["AfcSettings.InputDeviceId"] = "Windows recording device carrying real 12-kHz IF from the radio. Select IF, not demodulated AF.",
      ["AfcSettings.CaptureChannel"] = "Select the physical channel carrying the radio's 12-kHz USB IF. Mixing inverted stereo channels can cancel the signal.",
      ["AfcSettings.AcquisitionPreset"] = "Adaptive starts a short spectral window after about 250 ms and refines using overlapping multi-resolution FFTs. Fast minimizes delay; Balanced prioritizes stability; Sensitive increases integration for weak signals.",
      ["AfcSettings.Control"] = "Off disables AFC; Assist monitors without CAT frequency changes; Auto adjusts receive frequency after verification.",
      ["AfcSettings.InputSource"] = "Select direct Windows IF capture or the remote playback-loopback endpoint carrying real 12-kHz IF.",
      ["AfcSettings.LoopbackDeviceId"] = "Choose the remote playback/render endpoint containing real 12-kHz IF, not ordinary demodulated AF.",
      ["AfcSettings.IfReferenceHz"] = "IF center reference frequency used for AFC measurements, normally 12000 Hz.",
      ["AfcSettings.RfIfSign"] = "Zero determines RF/IF frequency polarity from radio mode and independent CAT/IF observations; ±1 is a manual override.",
      ["AfcSettings.TargetOffsetHz"] = "Search frequency for a CW or pilot reference relative to the 12-kHz IF center.",
      ["AfcSettings.FmReferenceCandidateHz"] = "Optional candidate FM signal frequency selected by clicking the IF spectrum; empty permits unrestricted search.",
      ["AfcSettings.DesiredOffsetHz"] = "Target offset for Center & Lock, independent of the acquisition search frequency.",
      ["AfcSettings.SsbPilotEnabled"] = "Enable only when a stable identifiable pilot or CW carrier exists; suppressed-carrier speech alone cannot establish absolute frequency.",
      ["AfcSettings.SpectrumRefLevelDb"] = "Visual reference level for the IF spectrum/waterfall only; it does not change PCM, AFC estimate or CAT frequency.",
      ["AfcSettings.MaxCorrectionHz"] = "Maximum AFC receive frequency correction in Hz.",
      ["AfcSettings.SignalLossPolicy"] = "HoldLast keeps the last verified receive correction through QSB while satellite Doppler tracking continues. GentleRelease returns slowly after 30 s without a signal.",
      ["AfcSettings.FmDeadbandHz"] = "FM frequency correction deadband in Hz to avoid unnecessary CAT writes.",
      ["AfcSettings.ToneDeadbandHz"] = "CW/SSB correction deadband in Hz to avoid unnecessary CAT writes."
    };

    internal static string? Name(string key) =>
      Names.TryGetValue(key, out string? value) ? value : null;

    internal static string? Description(string key) =>
      Help.TryGetValue(key, out string? value) ? value : null;
  }
}
