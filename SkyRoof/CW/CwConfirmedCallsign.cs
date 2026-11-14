using System.Text.RegularExpressions;

namespace SkyRoof.CW
{
  /// <summary>
  /// Conservative operator assistance only. Never creates a TX request:
  /// the user must explicitly choose a decoded, confirmed candidate.
  /// </summary>
  internal static partial class CwConfirmedCallsign
  {
    [GeneratedRegex(
      @"(?<![A-Z0-9/])[A-Z]{1,3}[0-9][A-Z]{1,4}(?:/[A-Z0-9]{1,5})?(?![A-Z0-9/])",
      RegexOptions.CultureInvariant)]
    private static partial Regex CandidatePattern();

    internal static string? LastCandidate(string committed)
    {
      MatchCollection matches = CandidatePattern().Matches(
        committed.ToUpperInvariant());
      return matches.Count > 0 ? matches[^1].Value : null;
    }
  }
}
