using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace SkyRoof
{
  internal class VersionChecker
  {
    // Include prereleases: GitHub's /releases/latest endpoint excludes them.
    internal const string ReleasesApiUrl =
      "https://api.github.com/repos/Iu-yang1/SkyRoof-Plus/releases?per_page=100";
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly LatestVersionInfo LatestVersion;
    public event EventHandler? UpdateAvailable;

    public VersionChecker(LatestVersionInfo latestVersion)
    {
      LatestVersion = latestVersion;
    }

    public async Task CheckVersionAsync()
    {
      var currentVersion = typeof(VersionChecker).Assembly.GetName().Version!;
      LatestVersion.Known = false;
      await GetAvailableVersion();

      if (!LatestVersion.Known)
      {
        Log.Warning("VersionChecker could not determine the latest fork release.");
        return;
      }

      if (LatestVersion.Major > currentVersion.Major ||
          (LatestVersion.Major == currentVersion.Major &&
           LatestVersion.Minor > currentVersion.Minor))
      {
        Log.Information("VersionChecker: fork update available: {Name}", LatestVersion.Name);
        UpdateAvailable?.Invoke(this, EventArgs.Empty);
      }
      else
        Log.Information("VersionChecker: fork is up to date: {Name}", LatestVersion.Name);
    }

    private static HttpClient CreateHttpClient()
    {
      var client = new HttpClient();
      client.DefaultRequestHeaders.UserAgent.ParseAdd("SkyRoof-Plus-Updater/1.0");
      client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
      return client;
    }

    private async Task GetAvailableVersion()
    {
      try
      {
        string json = await Http.GetStringAsync(ReleasesApiUrl);
        LatestVersionInfo? found = ParseLatestReleaseJson(json);
        if (found == null)
        {
          Log.Warning("VersionChecker found no published fork release with an installer.");
          return;
        }

        LatestVersion.Name = found.Name;
        LatestVersion.Url = found.Url;
        LatestVersion.Major = found.Major;
        LatestVersion.Minor = found.Minor;
        LatestVersion.Known = true;
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "VersionChecker could not retrieve GitHub fork releases.");
      }
    }

    // Public GitHub release metadata, not the upstream site's HTML.
    // A draft or a release without its matching Windows installer is not an update.
    internal static LatestVersionInfo? ParseLatestReleaseJson(string json)
    {
      using JsonDocument document = JsonDocument.Parse(json);
      if (document.RootElement.ValueKind != JsonValueKind.Array)
        return null;

      LatestVersionInfo? latest = null;
      foreach (JsonElement release in document.RootElement.EnumerateArray())
      {
        if (release.GetProperty("draft").GetBoolean())
          continue;

        string tag = release.GetProperty("tag_name").GetString() ?? "";
        Match match = Regex.Match(tag, @"^v\.(\d+)\.(\d+)$",
          RegexOptions.CultureInvariant);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out int major) ||
            !int.TryParse(match.Groups[2].Value, out int minor))
          continue;

        string installerName = $"SkyRoofSetup-{tag}.exe";
        JsonElement assets = release.GetProperty("assets");
        if (assets.ValueKind != JsonValueKind.Array ||
            !assets.EnumerateArray().Any(asset =>
              asset.GetProperty("name").GetString() == installerName))
          continue;

        string? url = release.GetProperty("html_url").GetString();
        if (string.IsNullOrWhiteSpace(url))
          continue;

        if (latest == null || major > latest.Major ||
            (major == latest.Major && minor > latest.Minor))
        {
          latest = new LatestVersionInfo
          {
            Known = true,
            Name = $"SkyRoof-Plus {tag}",
            Url = url,
            Major = major,
            Minor = minor
          };
        }
      }
      return latest;
    }
  }
}
