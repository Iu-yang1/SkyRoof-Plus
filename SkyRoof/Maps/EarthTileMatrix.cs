using System.Globalization;
using System.Xml.Linq;
using VE3NEA;

namespace SkyRoof
{
  internal readonly record struct EarthTileKey(int Level, int X, int Y, bool Labels = false)
  {
    internal int Width => 1 << Level;
    internal int Height => Width / 2;
    internal EarthTileKey Parent(int level) =>
      new(level, X >> (Level - level), Y >> (Level - level), Labels);
  }

  internal sealed record EarthTileMatrix(string Identifier, int Level)
  {
    internal int Width => 1 << Level;
    internal int Height => Width / 2;
  }

  // Level is log2(matrix width), NOT the provider's TileMatrix identifier.
  // Only global, 256-pixel, geographic 2:1 dyadic matrices are compatible
  // with the AEQD shader. Reject incompatible capabilities rather than
  // silently plotting a different region underneath the satellite.
  internal sealed class WmtsCapabilities
  {
    internal string Layer { get; }
    internal string Style { get; }
    internal string Format { get; }
    internal IReadOnlyDictionary<int, EarthTileMatrix> Matrices { get; }

    private WmtsCapabilities(string layer, string style, string format,
      IReadOnlyDictionary<int, EarthTileMatrix> matrices)
    {
      Layer = layer;
      Style = style;
      Format = format;
      Matrices = matrices;
    }

    internal static WmtsCapabilities Parse(string xml, string layerName)
    {
      var document = XDocument.Parse(xml);
      XNamespace wmts = "http://www.opengis.net/wmts/1.0";
      XNamespace ows = "http://www.opengis.net/ows/1.1";
      var contents = Require(document.Root?.Element(wmts + "Contents"), "WMTS Contents missing");
      var layer = Require(contents.Elements(wmts + "Layer")
        .FirstOrDefault(x => (string?)x.Element(ows + "Identifier") == layerName), "WMTS layer missing");
      var link = Require(layer.Elements(wmts + "TileMatrixSetLink")
        .FirstOrDefault(x => (string?)x.Element(wmts + "TileMatrixSet") == "c"), "WMTS geographic matrix link missing");
      var matrixSet = Require(contents.Elements(wmts + "TileMatrixSet")
        .FirstOrDefault(x => (string?)x.Element(ows + "Identifier") == "c"), "WMTS geographic matrix missing");
      var crs = (string?)matrixSet.Element(ows + "SupportedCRS") ?? "";
      if (!crs.EndsWith("4490", StringComparison.Ordinal))
        throw new InvalidDataException("WMTS matrix is not CGCS2000");

      var matrices = ReadMatrices(matrixSet, link, wmts, ows);
      var styles = layer.Elements(wmts + "Style").ToList();
      string style = (string?)(styles.FirstOrDefault(x =>
        (string?)x.Attribute("isDefault") is "true" or "1") ??
        styles.FirstOrDefault())?.Element(ows + "Identifier") ?? "default";
      string[] formats = layer.Elements(wmts + "Format").Select(x => x.Value).ToArray();
      string format = Require(formats.FirstOrDefault(x => x == "tiles") ??
        formats.FirstOrDefault(x => x == "image/png" || x == "image/jpeg"), "WMTS raster format missing");
      return new WmtsCapabilities(layerName, style, format, matrices);
    }

    private static T Require<T>(T? value, string error) where T : class
    {
      return value ?? throw new InvalidDataException(error);
    }

    private static Dictionary<int, EarthTileMatrix> ReadMatrices(
      XElement set, XElement link, XNamespace wmts, XNamespace ows)
    {
      var matrices = new Dictionary<int, EarthTileMatrix>();
      var limits = link.Element(wmts + "TileMatrixSetLimits");
      foreach (var matrix in set.Elements(wmts + "TileMatrix"))
      {
        string? id = (string?)matrix.Element(ows + "Identifier");
        int width = (int?)matrix.Element(wmts + "MatrixWidth") ?? 0;
        int height = (int?)matrix.Element(wmts + "MatrixHeight") ?? 0;
        if (string.IsNullOrEmpty(id) || width < 2 || width > (1 << 22) ||
            width != height * 2 || (width & (width - 1)) != 0 ||
            (int?)matrix.Element(wmts + "TileWidth") != 256 ||
            (int?)matrix.Element(wmts + "TileHeight") != 256 ||
            !IsGlobalOrigin((string?)matrix.Element(wmts + "TopLeftCorner")))
          continue;

        // A layer can advertise limits for only a subset of matrices.
        // V1 accepts complete global matrices, not regional grids.
        if (limits != null && !HasGlobalLimits(limits, wmts, id, width, height))
          continue;
        int level = System.Numerics.BitOperations.Log2((uint)width);
        matrices.Add(level, new EarthTileMatrix(id, level));
      }
      if (matrices.Count == 0)
        throw new InvalidDataException("No compatible global geographic WMTS matrices");
      return matrices;
    }

    private static bool HasGlobalLimits(XElement limits, XNamespace ns,
      string id, int width, int height)
    {
      var limit = limits.Elements(ns + "TileMatrixLimits")
        .FirstOrDefault(x => (string?)x.Element(ns + "TileMatrix") == id);
      return limit != null &&
        (int?)limit.Element(ns + "MinTileRow") == 0 &&
        (int?)limit.Element(ns + "MinTileCol") == 0 &&
        (int?)limit.Element(ns + "MaxTileRow") == height - 1 &&
        (int?)limit.Element(ns + "MaxTileCol") == width - 1;
    }

    private static bool IsGlobalOrigin(string? value)
    {
      var parts = (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length != 2 ||
          !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
          !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
        return false;
      return (Math.Abs(a + 180) < 1e-6 && Math.Abs(b - 90) < 1e-6) ||
        (Math.Abs(a - 90) < 1e-6 && Math.Abs(b + 180) < 1e-6);
    }
  }
}
