using System.ComponentModel;

namespace SkyRoof
{
  public enum OrbitEngineMode  public enum OrbitEngineMode
  {
    SgpNet = 0,
    Orekit = 1
  }

  public class OrbitSourceSettings
  {
    // Deserialize older SGP.NET preferences, but new installations and upgrades use Orekit exclusively.
    [Browsable(false)]
    [DefaultValue(OrbitEngineMode.Orekit)]
    public OrbitEngineMode Backend { get; set; } = OrbitEngineMode.Orekit;

    [Category("Orbit engine")]
    [DisplayName("Auto-download Orekit Data")]
    [Description("Automatically cache/update IERS Earth orientation data and a verified Orekit-format DE430 planetary ephemeris when required. Existing local files continue to work offline.")]
    [DefaultValue(true)]
    public bool AutoDownloadOrekitData { get; set; } = true;

    [Category("Orbit engine")]
    [DisplayName("Orekit Data Directory")]
    [Description("Optional directory containing tai-utc.dat and finals2000A.all. Blank uses the SkyRoof user-data/orekit-data directory.")]
    public string OrekitDataDirectory { get; set; } = string.Empty;

    [Category("Orbit engine")]
    [DisplayName("Active Orbit Backend")]
    [ReadOnly(true)]
    [Newtonsoft.Json.JsonIgnore]
    [Description("The currently initialized orbit engine, not merely the saved preference.")]
    public string ActiveBackend => OrbitBackendRegistry.CurrentBackendId;

    [Category("Orbit engine")]
    [DisplayName("Orbit Data Quality")]
    [ReadOnly(true)]
    [Newtonsoft.Json.JsonIgnore]
    [Description("Orekit time and IERS Earth orientation data coverage status.")]
    public string ActiveDataQuality => OrbitBackendRegistry.CurrentDataQuality;

    public const string DefaultSatellitesUrl =
      "https://db.satnogs.org/api/satellites/?format=json";
    public const string DefaultTransmittersUrl =
      "https://db.satnogs.org/api/transmitters/?format=json";
    public const string DefaultTleUrl =
      "https://db.satnogs.org/api/tle/?format=json";
    public const string DefaultCelestrakOmmCsvUrl =
      "https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=csv";
    public const string LegacyAutoTleUrl =
      "http://autotle.bi4pym.cn/AutoTLE.txt";
    // The old domain responds with HTTP 302 on some client paths.
    // Use the same upstream file that succeeded as a manual source in
    // the user's SO-50 operation log, with HTTPS and no redirect.
    public const string DefaultAutoTleUrl =
      "https://raw.githubusercontent.com/BI4PYM/AutoTLE/refs/heads/master/AutoTLE.txt";

    internal static string ResolveAutoTleUrl(string? configured)
    {
      string url = configured?.Trim() ?? string.Empty;
      return url.Equals(LegacyAutoTleUrl, StringComparison.OrdinalIgnoreCase)
        ? DefaultAutoTleUrl
        : url;
    }

    [Category("Satellite database")]    [Category("Satellite database")]
    [DisplayName("Satellite List URL")]
    [Description("Primary satellite-list endpoint. The default is SatNOGS. You may replace it with a compatible SatNOGS JSON endpoint. Leave blank to keep the last cached file instead of updating it.")]
    [DefaultValue(DefaultSatellitesUrl)]
    public string SatellitesUrl { get; set; } = DefaultSatellitesUrl;

    [Category("Satellite database")]
    [DisplayName("Transmitter List URL")]
    [Description("Primary transmitter-list endpoint. The default is SatNOGS. You may replace it with a compatible SatNOGS JSON endpoint. Leave blank to keep the last cached file instead of updating it.")]
    [DefaultValue(DefaultTransmittersUrl)]
    public string TransmittersUrl { get; set; } = DefaultTransmittersUrl;

    [Category("Orbit elements")]
    [DisplayName("CelesTrak OMM CSV URL")]
    [Description("Highest-priority automatic orbit source. The default is CelesTrak's amateur GP/OMM CSV feed. OMM is propagated directly; it is not converted back to legacy TLE.")]
    [DefaultValue(DefaultCelestrakOmmCsvUrl)]
    public string CelestrakOmmCsvUrl { get; set; } = DefaultCelestrakOmmCsvUrl;

    [Category("Orbit elements")]
    [DisplayName("Manual Orbit Source URLs")]
    [Description("User-configured URL or local-file orbit sources, separated by semicolons or new lines. These are below CelesTrak OMM CSV but above the built-in AutoTLE and SatNOGS sources. Earlier entries have higher priority. TLE text, SatNOGS JSON, and CelesTrak OMM JSON/CSV are supported.")]
    public string CustomTleSources { get; set; } = string.Empty;

    [Category("Orbit elements")]
    [DisplayName("AutoTLE URL")]
    [Description("Built-in AutoTLE fallback source. It is below Manual Orbit Source URLs and CelesTrak OMM CSV, but above the original SatNOGS orbit source. Leave blank to disable it.")]
    [DefaultValue(DefaultAutoTleUrl)]
    public string AutoTleUrl { get; set; } = DefaultAutoTleUrl;

    [Category("Orbit elements")]
    [DisplayName("SatNOGS Orbit URL")]
    [Description("Lowest-priority automatic orbit source. The default is the original SatNOGS TLE endpoint. Leave blank to disable updates from this source.")]
    [DefaultValue(DefaultTleUrl)]
    public string TleUrl { get; set; } = DefaultTleUrl;

    [Category("Orbit engine")]
    [DisplayName("Show Solar-System Targets")]    [Category("Orbit engine")]
    [DisplayName("Show Solar-System Targets")]
    [Description("Automatically include Moon, Sun and Venus using Orekit's planetary ephemerides when a compatible DE data file is present.")]
    [DefaultValue(true)]
    public bool ShowSolarSystemTargets { get; set; } = true;

    public override string ToString()    public override string ToString() => string.Empty;
  }
}
