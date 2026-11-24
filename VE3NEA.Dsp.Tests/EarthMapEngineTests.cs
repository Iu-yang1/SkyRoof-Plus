using System.Drawing;
using System.Drawing.Imaging;
using Newtonsoft.Json;
using SkyRoof;
using VE3NEA;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class EarthMapEngineTests
{
  private const string Capabilities = """
    <Capabilities xmlns="http://www.opengis.net/wmts/1.0"
      xmlns:ows="http://www.opengis.net/ows/1.1">
      <Contents>
        <Layer><ows:Identifier>vec</ows:Identifier>
          <Style isDefault="true"><ows:Identifier>default</ows:Identifier></Style>
          <Format>tiles</Format><TileMatrixSetLink><TileMatrixSet>c</TileMatrixSet></TileMatrixSetLink>
        </Layer>
        <TileMatrixSet><ows:Identifier>c</ows:Identifier>
          <ows:SupportedCRS>urn:ogc:def:crs:EPSG::4490</ows:SupportedCRS>
          <TileMatrix><ows:Identifier>zero</ows:Identifier><TopLeftCorner>90 -180</TopLeftCorner>
            <TileWidth>256</TileWidth><TileHeight>256</TileHeight>
            <MatrixWidth>2</MatrixWidth><MatrixHeight>1</MatrixHeight>
          </TileMatrix>
          <TileMatrix><ows:Identifier>one</ows:Identifier><TopLeftCorner>-180 90</TopLeftCorner>
            <TileWidth>256</TileWidth><TileHeight>256</TileHeight>
            <MatrixWidth>4</MatrixWidth><MatrixHeight>2</MatrixHeight>
          </TileMatrix>
          <TileMatrix><ows:Identifier>high</ows:Identifier><TopLeftCorner>90 -180</TopLeftCorner>
            <TileWidth>256</TileWidth><TileHeight>256</TileHeight>
            <MatrixWidth>262144</MatrixWidth><MatrixHeight>131072</MatrixHeight>
          </TileMatrix>
        </TileMatrixSet>
      </Contents>
    </Capabilities>
    """;

  [Fact]
  public void CapabilitiesUsesActualIdentifierAndMatrixDimensions()
  {
    var caps = WmtsCapabilities.Parse(Capabilities, "vec");
    Assert.Equal("zero", caps.Matrices[1].Identifier);
    Assert.Equal("one", caps.Matrices[2].Identifier);
    Assert.Equal("high", caps.Matrices[18].Identifier);
    string query = TiandituMapSource.TileQuery(caps, new EarthTileKey(2, 3, 1));
    Assert.Contains("TILEMATRIX=one&TILEROW=1&TILECOL=3", query);
    Assert.Contains("TILEMATRIXSET=c", query);
  }

  [Fact]
  public void RejectsIncompatibleProjectionAndMatrixOrigin()
  {
    Assert.Throws<InvalidDataException>(() =>
      WmtsCapabilities.Parse(Capabilities.Replace("4490", "3857"), "vec"));
    Assert.Throws<InvalidDataException>(() =>
      WmtsCapabilities.Parse(Capabilities.Replace("90 -180", "0 0")
        .Replace("-180 90", "0 0"), "vec"));
  }

  [Theory]
  [InlineData(0, -180, 0, 1)]
  [InlineData(0, 180, 0, 1)]
  [InlineData(90, 179, 3, 0)]
  [InlineData(-90, 179, 3, 1)]
  [InlineData(28, 116, 3, 0)]
  public void GeographicTileCoordinatesWrapAndClamp(double lat, double lon, int x, int y)
  {
    var key = EarthTileVisibility.At(new GeoPoint(lat, lon), 2);
    Assert.Equal(x, key.X);
    Assert.Equal(y, key.Y);
  }

  [Theory]
  [InlineData(0, 179.8)]
  [InlineData(89.9, 0)]
  [InlineData(-89.9, 179.8)]
  [InlineData(28, 116)]
  public void ViewportPlanIsBoundedAndCoversSampledView(double lat, double lon)
  {
    var center = new GeoPoint(lat, lon);
    int[] levels = Enumerable.Range(1, 18).ToArray();
    var size = new Size(1200, 600);
    var plan = EarthTileVisibility.Plan(center, size, 80, levels, levels.ToHashSet(), 256);
    Assert.NotEmpty(plan.Keys);
    Assert.InRange(plan.Keys.Count, 1, 256);
    var set = plan.Keys.ToHashSet();
    double radius = Math.PI / 80;
    for (int i = 0; i < 72; i++)
    {
      var point = center.MoveBy(new GeoPath
      {
        AzimuthRad = i * Math.PI / 36,
        DistanceRad = radius
      });
      var key = EarthTileVisibility.At(point, plan.Level);
      Assert.Contains(key, set);
      Assert.Contains(key with { Labels = true }, set);
      foreach (int parent in levels.Where(l => l < plan.Level))
        Assert.Contains(key.Parent(parent), set);
    }
  }

  [Fact]
  public void SparseIndexSeparatesLabelsAndBaseAcrossHighLevels()
  {
    var entries = Enumerable.Range(0, 256)
      .ToDictionary(i => new EarthTileKey(18, 123456 + i / 2, 54321, i % 2 == 0), i => i);
    var table = EarthTileIndex.Build(entries);
    foreach (var (key, slot) in entries)
    {
      int offset = (int)(EarthTileIndex.Hash(key) & (EarthTileIndex.Size - 1));
      int tag = key.Level * 2 + (key.Labels ? 1 : 0);
      while (table[offset * 4 + 3] != 0 &&
          (table[offset * 4] != key.X || table[offset * 4 + 1] != key.Y ||
            table[offset * 4 + 2] != tag))
        offset = (offset + 1) & (EarthTileIndex.Size - 1);
      Assert.Equal(slot + 1, table[offset * 4 + 3]);
    }
  }

  [Fact]
  public void TileDecoderPreservesNorthFirstBgraAndAlpha()
  {
    using var image = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
    image.SetPixel(0, 0, Color.FromArgb(128, 12, 34, 56));
    image.SetPixel(0, 255, Color.FromArgb(255, 91, 82, 73));
    using var stream = new MemoryStream();
    image.Save(stream, ImageFormat.Png);
    byte[] pixels = TiandituMapSource.DecodeTile(stream.ToArray());
    Assert.Equal(256 * 256 * 4, pixels.Length);
    Assert.Equal(new byte[] { 56, 34, 12, 128 }, pixels[..4]);
    Assert.Equal(new byte[] { 73, 82, 91, 255 }, pixels[(255 * 1024)..(255 * 1024 + 4)]);
  }

  [Fact]
  public void OfflineDefaultAndDpapiKeySerialization()
  {
    var settings = new EarthMapSettings();
    Assert.Equal(EarthMapSourceKind.BuiltIn, settings.Source);
    Assert.Equal(256, settings.EffectiveCapacity);
    settings.TiandituKey = "sample-local-key-for-roundtrip";
    string json = JsonConvert.SerializeObject(settings);
    Assert.DoesNotContain(settings.TiandituKey, json);
    Assert.Contains("dpapi:", json);
    var restored = JsonConvert.DeserializeObject<EarthMapSettings>(json);
    Assert.NotNull(restored);
    Assert.Equal(settings.TiandituKey, restored.TiandituKey);
  }

  private sealed class FakeSource : IMapSource
  {
    private readonly WmtsCapabilities caps = WmtsCapabilities.Parse(Capabilities, "vec");
    internal readonly TaskCompletionSource<byte[]> TileResult =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource<bool> Started =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<WmtsCapabilities> GetCapabilitiesAsync(bool labels, CancellationToken cancellation) =>
      Task.FromResult(caps);

    public async Task<byte[]> GetTileAsync(WmtsCapabilities capabilities,
      EarthTileKey tile, CancellationToken cancellation)
    {
      Started.TrySetResult(true);
      return await TileResult.Task.WaitAsync(cancellation);
    }
  }

  [Fact]
  public async Task SwitchingSourcesCancelsOldTilesAndSeparatesEpochs()
  {
    var oldSource = new FakeSource();
    var newSource = new FakeSource();
    using var manager = new MapSourceManager(() => { },
      (kind, _) => kind == EarthMapSourceKind.TiandituVector ? oldSource : newSource);
    var settings = new EarthMapSettings
    {
      Source = EarthMapSourceKind.TiandituVector, TiandituKey = "test", Labels = false
    };
    var center = new GeoPoint(0, 0);
    var size = new Size(360, 360);
    manager.Configure(settings);
    int oldEpoch = manager.Epoch;
    await PumpUntil(manager, center, size, () => oldSource.Started.Task.IsCompleted);
    settings.Source = EarthMapSourceKind.TiandituImagery;
    manager.Configure(settings);
    Assert.True(manager.Epoch > oldEpoch);
    Assert.Empty(manager.Tiles);
    oldSource.TileResult.TrySetResult([1]);
    await PumpUntil(manager, center, size, () => newSource.Started.Task.IsCompleted);
    newSource.TileResult.TrySetResult([2]);
    await PumpUntil(manager, center, size, () => manager.ReadyCount > 0);
    Assert.All(manager.Tiles.Values, value => Assert.Equal(new byte[] { 2 }, value));
    settings.Source = EarthMapSourceKind.BuiltIn;
    manager.Configure(settings);
    Assert.Equal(EarthMapStatus.Offline, manager.Status);
    Assert.Empty(manager.Tiles);
  }

  private static async Task PumpUntil(MapSourceManager manager, GeoPoint center,
    Size size, Func<bool> condition)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition())
    {
      timeout.Token.ThrowIfCancellationRequested();
      manager.Update(center, size, 1, 256);
      await Task.Delay(10, timeout.Token);
    }
  }
}
