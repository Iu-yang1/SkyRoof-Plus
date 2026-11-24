namespace SkyRoof
{
  internal static class EarthMapProviders
  {
    internal static bool IsAmap(EarthMapSourceKind kind) =>
      kind is EarthMapSourceKind.AmapVector or EarthMapSourceKind.AmapImagery;

    internal static bool IsGoogle(EarthMapSourceKind kind) =>
      kind is EarthMapSourceKind.GoogleRoad or EarthMapSourceKind.GoogleSatellite or
        EarthMapSourceKind.GoogleTerrain or EarthMapSourceKind.GoogleTerrainLabels;

    internal static bool IsXyz(EarthMapSourceKind kind) => IsAmap(kind) || IsGoogle(kind);

    internal static bool HasSeparateLabels(EarthMapSourceKind kind) =>
      kind is EarthMapSourceKind.TiandituVector or EarthMapSourceKind.TiandituImagery or
        EarthMapSourceKind.TiandituTerrain or EarthMapSourceKind.AmapImagery;

    internal static EarthTileProjection Projection(EarthMapSourceKind kind) => kind switch
    {
      EarthMapSourceKind.AmapVector or EarthMapSourceKind.AmapImagery or
        EarthMapSourceKind.GoogleRoad or EarthMapSourceKind.GoogleTerrainLabels => EarthTileProjection.GcjMercator,
      EarthMapSourceKind.GoogleSatellite or EarthMapSourceKind.GoogleTerrain => EarthTileProjection.Mercator,
      _ => EarthTileProjection.Geographic
    };

    internal static string ProviderName(EarthMapSourceKind kind) =>
      IsAmap(kind) ? SettingsLanguage.Text("高德地图", "Amap") :
      IsGoogle(kind) ? SettingsLanguage.Text("Google 地图", "Google Maps") :
      SettingsLanguage.Text("天地图", "Tianditu");
  }

  // Experimental public-facing XYZ URLs supplied by the user, not official
  // developer API contracts. No authentication or spoofed client headers.
  internal sealed class XyzMapSource : IMapSource
  {
    private readonly EarthMapSourceKind kind;

    internal XyzMapSource(EarthMapSourceKind kind)
    {
      if (!EarthMapProviders.IsXyz(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
      this.kind = kind;
    }

    internal static Uri TileUri(EarthMapSourceKind kind, EarthTileKey tile)
    {
      string xyz = FormattableString.Invariant($"x={tile.X}&y={tile.Y}&z={tile.Level}");
      string prefix = kind switch
      {
        EarthMapSourceKind.AmapVector when !tile.Labels =>
          "https://webrd04.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=7&",
        EarthMapSourceKind.AmapImagery when tile.Labels =>
          "https://webst01.is.autonavi.com/appmaptile?style=8&",
        EarthMapSourceKind.AmapImagery =>
          "https://webst01.is.autonavi.com/appmaptile?style=6&",
        EarthMapSourceKind.GoogleRoad when !tile.Labels =>
          "https://mt1.google.com/vt/lyrs=m&gl=cn&",
        EarthMapSourceKind.GoogleSatellite when !tile.Labels =>
          "https://mt1.google.com/vt/lyrs=s&",
        EarthMapSourceKind.GoogleTerrain when !tile.Labels =>
          "https://mt1.google.com/vt/lyrs=t&",
        EarthMapSourceKind.GoogleTerrainLabels when !tile.Labels =>
          "https://mt1.google.com/vt/lyrs=p&gl=cn&",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
      };
      return new Uri(prefix + xyz);
    }

    public Task<WmtsCapabilities> GetCapabilitiesAsync(bool labels, CancellationToken cancellation)
    {
      cancellation.ThrowIfCancellationRequested();
      if (labels && !EarthMapProviders.HasSeparateLabels(kind))
        throw new NotSupportedException("No compatible separate XYZ labels");
      // Conservative configured maximum, not a verified provider guarantee.
      return Task.FromResult(WmtsCapabilities.CreateXyz(kind.ToString(), 18));
    }

    public async Task<byte[]> GetTileAsync(WmtsCapabilities capabilities,
      EarthTileKey tile, CancellationToken cancellation)
    {
      byte[] bytes = await TiandituMapSource.ReadAsync(TileUri(kind, tile),
        2 * 1024 * 1024, cancellation).ConfigureAwait(false);
      return TiandituMapSource.DecodeTile(bytes);
    }
  }
}
