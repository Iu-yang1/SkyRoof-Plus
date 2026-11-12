using System.ComponentModel;

namespace SkyRoof
{
  public enum OrbitEngineMode
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

    public const string DefaultOrekitEopSources =
      "https://maia.usno.navy.mil/ser7/finals2000A.all;" +
      "https://datacenter.iers.org/products/eop/rapid/standard/finals2000A.all";

    public const string DefaultOrekitDe430Url =
      "https://raw.githubusercontent.com/eSpace-epfl/orekit-data/master/DE-430-ephemerides/lnxp1990.430";

    [Category("Orekit ephemeris")]
    [DisplayName("Auto-download Orekit Data")]
    [Description("Download missing Orekit data before engine initialization and refresh IERS EOP data every seven days. Valid cached data remain available offline.")]
    [DefaultValue(true)]
    public bool AutoDownloadOrekitData { get; set; } = true;

    [Category("Orekit ephemeris")]
    [DisplayName("Show Solar-System Targets")]
    [Description("Show Moon, Sun and Venus using the Orekit JPL DE430 ephemeris. Restart SkyRoof after changing this option.")]
    [DefaultValue(true)]
    public bool ShowSolarSystemTargets { get; set; } = true;

    [Category("Orekit ephemeris")]
    [DisplayName("Orekit Data Directory")]
    [Description("Directory for tai-utc.dat, IERS finals2000A.all, and Orekit DE430 lnxp1990.430. Leave blank for SkyRoof user-data/orekit-data; changing requires a restart.")]
    public string OrekitDataDirectory { get; set; } = string.Empty;

    [Category("Orekit ephemeris")]
    [DisplayName("IERS EOP Download Sources")]
    [Description("Ordered HTTPS URLs for finals2000A.all, separated by semicolons or new lines. Used for initial download and seven-day EOP refresh.")]
    [DefaultValue(DefaultOrekitEopSources)]
    public string OrekitEopSources { get; set; } = DefaultOrekitEopSources;

    [Category("Orekit ephemeris")]
    [DisplayName("JPL DE430 Download URL")]
    [Description("Orekit-native DE430 data URL. Downloaded data must match the pinned upstream Git blob hash; NAIF BSP/SPK files are not interchangeable.")]
    [DefaultValue(DefaultOrekitDe430Url)]
    public string OrekitDe430Url { get; set; } = DefaultOrekitDe430Url;

    [Category("Orekit ephemeris")]
    [DisplayName("Orekit Data Status")]
    [ReadOnly(true)]
    [Newtonsoft.Json.JsonIgnore]
    [Description("Resolved local directory and file-presence diagnostics. The running data context changes only after restart.")]
    public string DataStatus
    {
      get
      {
        string directory = OrbitBackendRegistry.ResolveDataDirectory(OrekitDataDirectory);
        return $"UTC: {(File.Exists(Path.Combine(directory, "tai-utc.dat")) || File.Exists(Path.Combine(directory, "UTC-TAI.history")) ? "present" : "missing")}; " +
          $"EOP: {(File.Exists(Path.Combine(directory, "finals2000A.all")) ? "present" : "missing")}; " +
          $"DE430: {(File.Exists(Path.Combine(directory, "lnxp1990.430")) ? "present" : "missing")}";
      }
    }

    [Category("Orekit ephemeris")]
    [DisplayName("Active Orbit Backend")]
    [ReadOnly(true)]
    [Newtonsoft.Json.JsonIgnore]
    [Description("The currently initialized orbit engine, not merely the saved preference.")]
    public string ActiveBackend => OrbitBackendRegistry.CurrentBackendId;

    [Category("Orekit ephemeris")]
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

    [Category("Satellite database")]
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

    public override string ToString() => string.Empty;
  }
}
