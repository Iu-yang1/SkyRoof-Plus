using System.Text.RegularExpressions;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Satellites
{
  internal partial class SignalParamsResolver
  {
    // Decoder identity is independent of the catalog number assigned to
    // a recently launched satellite. Keep the real DB identity for orbit,
    // archives and uploads; only select the protocol with this predicate.
    private static bool IsUnneName(string? name) =>
      !string.IsNullOrWhiteSpace(name) &&
      Regex.IsMatch(name.Trim(), @"^UNNE[-_ ]?1B$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsUnneSatellite(SatnogsDbSatellite? sat) =>
      sat != null && (sat.norad_cat_id == Unne1bDeframer.NoradId ||
        IsUnneName(sat.name) || IsUnneName(sat.sat_id) ||
        Regex.Split(sat.names ?? "", @"[,;|\r\n]+").Any(IsUnneName) ||
        sat.AllNames.Any(IsUnneName));

    internal static bool IsUnne1bDownlink(SatnogsDbTransmitter? tx,
      SatnogsDbSatellite? satellite = null) =>
      tx?.downlink_low == 436888000L &&
      (tx.norad_cat_id == Unne1bDeframer.NoradId ||
       IsUnneName(tx.sat_id) || IsUnneSatellite(tx.Satellite) ||
       IsUnneSatellite(satellite));

    // Canonical protocol key for the existing native/fallback DSP dispatch,
    // not a replacement catalog ID for SatNOGS or orbit tracking.
    internal static int? TelemetryDecoderNoradId(SatnogsDbSatellite? satellite,
      SatnogsDbTransmitter? tx, SignalParams signalParams) =>
      IsUnne1bDownlink(tx, satellite) && signalParams.Framing == Framing.HADES
        ? Unne1bDeframer.NoradId : satellite?.norad_cat_id;

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

    internal static bool IsUnne1bFmRepeater(SatnogsDbTransmitter? tx,
      SatnogsDbSatellite? satellite = null)
    {
      if (tx == null || !IsUnne1bDownlink(tx, satellite)) return false;
      // The operator-selected primary mode wins over the secondary
      // DownlinkMode imported by SatNOGS. A BPSK row must not become a
      // voice repeater merely because its secondary metadata says FM.
      Modulation selected = ExtractyModulation(tx.mode);
      if (selected is Modulation.BPSK or Modulation.FSK or Modulation.CW or Modulation.SSTV) return false;
      if (selected == Modulation.FM) return true;
      if (string.Equals(tx.DownlinkMode, "FM", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(tx.mode, "FMN", StringComparison.OrdinalIgnoreCase))
        return true;
      string description = tx.description ?? string.Empty;
      return description.Contains("FM", StringComparison.OrdinalIgnoreCase) &&
        (description.Contains("Repeater", StringComparison.OrdinalIgnoreCase) ||
         description.Contains("Voice", StringComparison.OrdinalIgnoreCase));
    }

    // Used only when an UNNE FM row has no usable database telemetry sibling.
    // It never changes CAT or the selected transmitter's FM classification.
    internal static SignalParams Unne1bFmTelemetryParams() =>
      new(200, Modulation.FSK, Framing.HADES, AudioSampleRate, 820);

  }
}
