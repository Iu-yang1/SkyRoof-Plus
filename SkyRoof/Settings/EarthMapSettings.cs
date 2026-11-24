using System.ComponentModel;
using Newtonsoft.Json;

namespace SkyRoof
{
  public enum EarthMapSourceKind { BuiltIn, TiandituVector, TiandituImagery, TiandituTerrain }

  public sealed class EarthMapSettings
  {
    [DisplayName("Default map source")]
    public EarthMapSourceKind Source { get; set; } = EarthMapSourceKind.BuiltIn;

    [DisplayName("Tianditu API Key")]
    [Description("Use a Tianditu service key authorized for native desktop WMTS access. Stored using Windows DPAPI.")]
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
    [DefaultValue(true)]
    public bool Labels { get; set; } = true;

    [DisplayName("GPU tile pool capacity")]
    [Description("Number of 256 x 256 RGBA tile slots, shared by base map and labels. 256 slots use 64 MiB.")]
    public int TileCapacity { get; set; } = 256;

    internal int EffectiveCapacity => Math.Clamp(TileCapacity, 32, 512);
    public override string ToString() => string.Empty;
  }
}
