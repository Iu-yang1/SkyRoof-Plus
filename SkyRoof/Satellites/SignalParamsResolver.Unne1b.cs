using System.Text.RegularExpressions;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Satellites
{
  internal partial class SignalParamsResolver
  {
    // Authoritative local protocol key: satellite NORAD plus this exact
    // shared downlink. SatNOGS mode/baud/free text are fallible metadata.
    internal static bool IsUnne1bDownlink(SatnogsDbTransmitter? tx) =>
      tx != null &&
      (tx.norad_cat_id == Unne1bDeframer.NoradId ||
       tx.Satellite?.norad_cat_id == Unne1bDeframer.NoradId) &&
      tx.downlink_low == 436888000L;

    internal static bool IsUnne1b200BaudTelemetry(SatnogsDbTransmitter? tx)
    {
      if (tx == null || !IsUnne1bDownlink(tx) || IsUnne1bFmRepeater(tx)) return false;
      // Never rewrite actual CW/SSTV transmitters. Accept the 200-baud
      // BPSK row even if its DB baud/description has been left blank, and
      // do not let a wrong gr-satellites modulation veto a known downlink.
      Modulation rawMode = ExtractyModulation($"{tx.mode} {tx.DownlinkMode}");
      if (rawMode is Modulation.CW or Modulation.SSTV) return false;
      if (rawMode is Modulation.FSK or Modulation.BPSK) return true;
      Modulation descriptionMode = ExtractyModulation(tx.description);
      if (descriptionMode is Modulation.CW or Modulation.SSTV) return false;
      return descriptionMode is Modulation.FSK or Modulation.BPSK ||
             tx.baud == 200 || tx.gr_sats?.baudrate == 200 ||
             Regex.IsMatch(tx.description ?? "", @"\b200\s*(?:bps|baud)\b",
               RegexOptions.IgnoreCase);
    }

    internal static bool IsUnne1bFmRepeater(SatnogsDbTransmitter? tx)
    {
      if (tx == null || !IsUnne1bDownlink(tx)) return false;
      return string.Equals(tx.mode, "FM", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tx.DownlinkMode, "FM", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tx.mode, "FMN", StringComparison.OrdinalIgnoreCase) ||
        ((tx.description?.Contains("FM", StringComparison.OrdinalIgnoreCase) ?? false) &&
         (tx.description.Contains("Repeater", StringComparison.OrdinalIgnoreCase) ||
          tx.description.Contains("Voice", StringComparison.OrdinalIgnoreCase)));
    }

    // Used only when an UNNE FM row has no usable database telemetry sibling.
    // It never changes CAT or the selected transmitter's FM classification.
    internal static SignalParams Unne1bFmTelemetryParams() =>
      new(200, Modulation.FSK, Framing.HADES, AudioSampleRate, 820);

  }
}
