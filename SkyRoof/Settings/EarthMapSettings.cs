using System.ComponentModel;
using Newtonsoft.Json;

namespace SkyRoof
{
  public enum EarthMapSourceKind
  {
    BuiltIn, TiandituVector, TiandituImagery, TiandituTerrain,
    AmapVector, AmapImagery, GoogleRoad, GoogleSatellite, GoogleTerrain,
    GoogleTerrainLabels
  }

  public sealed class EarthMapSettings
  {
    [DisplayName("Default map source")]
    [Description("Select the built-in map, Tianditu WMTS, or experimental Amap/Google XYZ layers. XYZ endpoints are unofficial, require no key, and may change or be unavailable. Check provider terms before use.")]
    public EarthMapSourceKind Source { get; set; } = EarthMapSourceKind.BuiltIn;

    [DisplayName("Tianditu API Key")]
    [Description("Register and verify your developer account at lbs.tianditu.gov.cn. In Console > Application management > Create application, select Server-side as recommended for native HTTPS requests, authorize Map Service (WMTS), and copy the application key (tk). Configure the public egress IP if required by the service. No browser JS API is used. Stored using Windows DPAPI.")]
    [PasswordPropertyText(true)]
    [JsonIgnore]
    public string TiandituKey { get; set; } = "";

    [Browsable(false)]
    [JsonProperty("TiandituKeyProtected")]
    public string TiandituKeyProtected
    {
      get => SecretProtector.Protect(TiandituKey);
      set => TiandituKey = SecretProtector.Unprotect(value);
    }

    [DisplayName("Show map labels")]
    [Description("Overlay compatible separate labels on Tianditu or Amap imagery. Other XYZ layers may already include labels; Google mixed-coordinate hybrid is not used.")]
    [DefaultValue(true)]
    public bool Labels { get; set; } = true;

    [DisplayName("GPU tile pool capacity")]
    [Description("Number of 256 x 256 RGBA tile slots, shared by base map and labels. 256 slots use 64 MiB.")]
    public int TileCapacity { get; set; } = 256;

    internal int EffectiveCapacity => Math.Clamp(TileCapacity, 32, 512);
    public override string ToString() => string.Empty;
  }
}
