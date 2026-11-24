using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace SkyRoof
{
  internal interface IMapSource
  {
    Task<WmtsCapabilities> GetCapabilitiesAsync(bool labels, CancellationToken cancellation);
    Task<byte[]> GetTileAsync(WmtsCapabilities capabilities, EarthTileKey tile, CancellationToken cancellation);
  }

  internal sealed class TiandituMapSource : IMapSource
  {
    private readonly EarthMapSourceKind source;
    private readonly string key;

    internal TiandituMapSource(EarthMapSourceKind source, string key)
    {
      this.source = source;
      this.key = key;
    }

    private static readonly HttpClient Client = new(new HttpClientHandler
    {
      AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(15) };

    internal static (string Base, string Labels) Layers(EarthMapSourceKind source) => source switch
    {
      EarthMapSourceKind.TiandituVector => ("vec", "cva"),
      EarthMapSourceKind.TiandituImagery => ("img", "cia"),
      EarthMapSourceKind.TiandituTerrain => ("ter", "cta"),
      _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private string Layer(bool labels) => labels ? Layers(source).Labels : Layers(source).Base;

    private Uri BuildUri(string layer, string query) => new(
      $"https://t0.tianditu.gov.cn/{layer}_c/wmts?SERVICE=WMTS&{query}&tk={Uri.EscapeDataString(key)}");

    internal static string TileQuery(WmtsCapabilities capabilities, EarthTileKey tile)
    {
      string matrix = Uri.EscapeDataString(capabilities.Matrices[tile.Level].Identifier);
      return FormattableString.Invariant(
        $"REQUEST=GetTile&VERSION=1.0.0&LAYER={Uri.EscapeDataString(capabilities.Layer)}&STYLE={Uri.EscapeDataString(capabilities.Style)}&TILEMATRIXSET=c&FORMAT={Uri.EscapeDataString(capabilities.Format)}&TILEMATRIX={matrix}&TILEROW={tile.Y}&TILECOL={tile.X}");
    }

    public async Task<WmtsCapabilities> GetCapabilitiesAsync(bool labels, CancellationToken cancellation)
    {
      string layer = Layer(labels);
      byte[] bytes = await ReadAsync(BuildUri(layer, "REQUEST=GetCapabilities&VERSION=1.0.0"),
        2 * 1024 * 1024, cancellation).ConfigureAwait(false);
      return WmtsCapabilities.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'), layer);
    }

    public async Task<byte[]> GetTileAsync(WmtsCapabilities capabilities,
      EarthTileKey tile, CancellationToken cancellation)
    {
      byte[] bytes = await ReadAsync(BuildUri(capabilities.Layer, TileQuery(capabilities, tile)),
        2 * 1024 * 1024, cancellation).ConfigureAwait(false);
      return DecodeTile(bytes);
    }

    // Return decoded BGRA, top row first; reject service exception documents
    // and incorrectly-sized images. No persistent tile cache is created.
    internal static byte[] DecodeTile(byte[] encoded)
    {
      using var stream = new MemoryStream(encoded);
      using var image = Image.FromStream(stream, false, true);
      if (image.Width != 256 || image.Height != 256)
        throw new InvalidDataException("Unexpected WMTS tile dimensions");
      using var bitmap = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
      using (var graphics = Graphics.FromImage(bitmap))
      {
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(image, 0, 0);
      }
      var data = bitmap.LockBits(new Rectangle(0, 0, 256, 256),
        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try
      {
        var result = new byte[256 * 256 * 4];
        for (int y = 0; y < 256; y++)
          Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), result, y * 1024, 1024);
        return result;
      }
      finally { bitmap.UnlockBits(data); }
    }

    private static async Task<byte[]> ReadAsync(Uri uri, int limit, CancellationToken cancellation)
    {
      // Do not propagate HTTP exceptions containing a URL with the API key.
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
      timeout.CancelAfter(TimeSpan.FromSeconds(15));
      using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
        timeout.Token).ConfigureAwait(false);
      if (response.StatusCode == HttpStatusCode.TooManyRequests)
        throw new InvalidDataException("WMTS request quota exceeded");
      if (!response.IsSuccessStatusCode ||
          response.Content.Headers.ContentLength > limit)
        throw new InvalidDataException("WMTS response unavailable");
      using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
      using var output = new MemoryStream();
      var buffer = new byte[16384];
      int count;
      while ((count = await input.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
      {
        if (output.Length + count > limit)
          throw new InvalidDataException("WMTS response too large");
        output.Write(buffer, 0, count);
      }
      return output.ToArray();
    }
  }
}
