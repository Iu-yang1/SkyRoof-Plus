using System.Drawing;
using SkyRoof;
using VE3NEA;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class EarthMapXyzTests
{
  [Theory]
  [InlineData(EarthMapSourceKind.AmapVector, false, "https://webrd04.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=7&x=3&y=2&z=4")]
  [InlineData(EarthMapSourceKind.AmapImagery, false, "https://webst01.is.autonavi.com/appmaptile?style=6&x=3&y=2&z=4")]
  [InlineData(EarthMapSourceKind.AmapImagery, true, "https://webst01.is.autonavi.com/appmaptile?style=8&x=3&y=2&z=4")]
  [InlineData(EarthMapSourceKind.GoogleRoad, false, "https://mt1.google.com/vt/lyrs=m&gl=cn&x=3&y=2&z=4")]
  [InlineData(EarthMapSourceKind.GoogleSatellite, false, "https://mt1.google.com/vt/lyrs=s&x=3&y=2&z=4")]
  [InlineData(EarthMapSourceKind.GoogleTerrain, false, "https://mt1.google.com/vt/lyrs=t&x=3&y=2&z=4")]
  [InlineData(EarthMapSourceKind.GoogleTerrainLabels, false, "https://mt1.google.com/vt/lyrs=p&gl=cn&x=3&y=2&z=4")]
  public void ArticleUrlsUseNativeXyzWithoutKey(EarthMapSourceKind kind, bool labels, string expected)
  {
    Assert.Equal(expected, XyzMapSource.TileUri(kind, new EarthTileKey(4, 3, 2, labels)).AbsoluteUri);
  }

  [Theory]
  [InlineData(0, 0, 0, 0, 0)]
  [InlineData(0, 0, 2, 2, 2)]
  [InlineData(85.0511287798066, 179, 2, 3, 0)]
  [InlineData(-85.0511287798066, 179, 2, 3, 3)]
  [InlineData(0, 180, 2, 0, 2)]
  [InlineData(0, -180, 2, 0, 2)]
  public void MercatorUsesSquareMatricesAndWrapsLongitude(double lat, double lon, int z, int x, int y)
  {
    var actual = EarthTileVisibility.At(new GeoPoint(lat, lon), z, EarthTileProjection.Mercator);
    Assert.Equal(new EarthTileKey(z, x, y), actual);
  }

  [Fact]
  public void GcjForwardShiftsBeijingButNotLondon()
  {
    var beijing = Gcj02.Forward(new GeoPoint(39.908823, 116.39747));
    Assert.InRange(beijing.Latitude, 39.9102264, 39.9102266);
    Assert.InRange(beijing.Longitude, 116.4037134, 116.4037138);
    var london = new GeoPoint(51.5074, -0.1278);
    Assert.Equal(london.Latitude, Gcj02.Forward(london).Latitude);
    Assert.Equal(london.Longitude, Gcj02.Forward(london).Longitude);
  }

  [Theory]
  [InlineData(EarthMapSourceKind.AmapVector, 2)]
  [InlineData(EarthMapSourceKind.AmapImagery, 2)]
  [InlineData(EarthMapSourceKind.GoogleRoad, 2)]
  [InlineData(EarthMapSourceKind.GoogleSatellite, 1)]
  [InlineData(EarthMapSourceKind.GoogleTerrain, 1)]
  [InlineData(EarthMapSourceKind.GoogleTerrainLabels, 2)]
  public async Task XyzSourcesConfigureWithoutTiandituKey(EarthMapSourceKind kind, int projection)
  {
    using var manager = new MapSourceManager(() => { });
    var config = new EarthMapSettings { Source = kind };
    manager.Configure(config);
    Assert.True(manager.HasSource);
    Assert.Equal(EarthMapStatus.Loading, manager.Status);
    Assert.Equal((EarthTileProjection)projection, manager.Projection);
    var source = new XyzMapSource(kind);
    var caps = await source.GetCapabilitiesAsync(false, CancellationToken.None);
    Assert.Equal(1, caps.Matrices[0].Width);
    Assert.Equal(1, caps.Matrices[0].Height);
    Assert.Equal(256, caps.Matrices[8].Height);
    Assert.Equal("18", caps.Matrices[18].Identifier);
    config.Source = EarthMapSourceKind.TiandituVector;
    manager.Configure(config);
    Assert.False(manager.HasSource);
    Assert.Equal(EarthMapStatus.KeyMissing, manager.Status);
  }

  [Theory]
  [InlineData(28, 116, 80)]
  [InlineData(39.908823, 116.39747, 16384)]
  [InlineData(0, 179.9, 128)]
  [InlineData(84.9, -179.9, 80)]
  [InlineData(55.8271, 120, 16384)]
  [InlineData(25, 72.004, 16384)]
  public void GcjPlannerIncludesActualVisibleTiles(double lat, double lon, double zoom)
  {
    var center = new GeoPoint(lat, lon);
    var viewport = new Size(1200, 600);
    var camera = new EarthViewCamera();
    camera.Reset(center);
    int[] levels = Enumerable.Range(0, 19).ToArray();
    var options = new EarthTilePlanOptions(levels, levels.ToHashSet(), 256, EarthTileProjection.GcjMercator);
    var plan = EarthTileVisibility.Plan(center, viewport, zoom, options);
    Assert.InRange(plan.Keys.Count, 2, 256);
    var keys = plan.Keys.ToHashSet();
    Assert.Contains(new EarthTileKey(0, 0, 0), keys);
    for (int y = 0; y <= viewport.Height; y += 60)
      for (int x = 0; x <= viewport.Width; x += 60)
      {
        if (!camera.TryUnproject(new Point(x,y), viewport, zoom, out var point) ||
            Math.Abs(point.Latitude) > EarthTileGrid.MercatorLimit) continue;
        var tile = EarthTileVisibility.At(point, plan.Level, options.Projection);
        Assert.Contains(tile, keys);
        Assert.Contains(tile with { Labels = true }, keys);
        Assert.Contains(tile.Parent(0), keys);
      }
  }

  [Fact]
  public void TileFailureDiagnosticsDoNotExposeRequestSecrets()
  {
    var dns = new System.Net.Http.HttpRequestException(
      System.Net.Http.HttpRequestError.NameResolutionError, "secret-key");
    Assert.Equal("NameResolutionError", MapSourceManager.DescribeTileFailure(dns));
    var forbidden = new System.Net.Http.HttpRequestException(
      "secret-key", null, System.Net.HttpStatusCode.Forbidden);
    Assert.Equal("HTTP 403", MapSourceManager.DescribeTileFailure(forbidden));
    Assert.Equal("Timeout", MapSourceManager.DescribeTileFailure(new TaskCanceledException("secret-key")));
  }

  [Fact]
  public void MixedCoordinateGoogleLabelsAreNotSilentlyOverlaid()
  {
    Assert.False(EarthMapProviders.HasSeparateLabels(EarthMapSourceKind.GoogleSatellite));
    Assert.True(EarthMapProviders.HasSeparateLabels(EarthMapSourceKind.AmapImagery));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      XyzMapSource.TileUri(EarthMapSourceKind.GoogleSatellite, new EarthTileKey(4, 3, 2, true)));
  }
}
