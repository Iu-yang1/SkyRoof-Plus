namespace SkyRoof
{
  // Satellite identity is distinct from temporary SatNOGS catalogue numbers.
  // Keep this narrowly scoped to the positively identified UNNE-1B / HADES-E2
  // satellite; never rewrite arbitrary records solely because they use 70002.
  internal static class Unne1bSatelliteIdentity
  {
    internal const int OfficialNoradId = 101101;
    internal const int SatnogsProvisionalId = 98205;
    internal const int SecondaryProvisionalId = 70002;

    private static readonly System.Text.RegularExpressions.Regex Names = new(
      @"(?<![A-Za-z0-9])(?:UNNE[-_ ]?1B|HADES[-_ ]?E2|SO[-_ ]?131)(?![A-Za-z0-9])",
      System.Text.RegularExpressions.RegexOptions.IgnoreCase |
      System.Text.RegularExpressions.RegexOptions.CultureInvariant |
      System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static bool HasName(string? text) =>
      !string.IsNullOrWhiteSpace(text) && Names.IsMatch(text);

    internal static bool IsSatellite(SatnogsDbSatellite? satellite) =>
      satellite != null &&
      (satellite.norad_cat_id == OfficialNoradId ||
       HasName(satellite.name) ||
       HasName(satellite.names) ||
       satellite.AllNames.Any(HasName));

    internal static bool IsProvisionalId(int? number) =>
      number is SatnogsProvisionalId or SecondaryProvisionalId;
  }
}
