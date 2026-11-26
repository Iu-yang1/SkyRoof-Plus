using SharpGL;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;
using VE3NEA;

namespace SkyRoof
{
  internal readonly record struct EarthMapFrame(GeoPoint Center, GeoPoint Satellite,
    double Footprint, Size Viewport, double Zoom, float Brightness);

  internal sealed class EarthMapRenderer
  {
    private readonly OpenGL gl;
    private readonly uint builtinTexture;
    private readonly ShaderProgram program = new();
    private readonly VertexBufferArray vao = new();
    private GpuEarthTilePool pool;

    internal int Capacity => pool.Capacity;
    internal int RequestedCapacity { get; private set; }

    internal EarthMapRenderer(OpenGL gl, uint builtinTexture, int capacity)
    {
      this.gl = gl;
      this.builtinTexture = builtinTexture;
      RequestedCapacity = capacity;
      vao.Create(gl);
      program.Create(gl, EarthMapShaders.Vertex, EarthMapShaders.Fragment, null);
      program.Bind(gl);
      SetInt("worldMapTexture", 0);
      SetInt("tileTexture", 1);
      SetInt("tileIndex", 2);
      pool = new GpuEarthTilePool(gl, capacity);
      pool.Bind();
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, builtinTexture);
      vao.Bind(gl);
      program.AssertValid(gl);
      vao.Unbind(gl);
      program.Unbind(gl);
    }

    internal void SetCapacity(int capacity)
    {
      if (RequestedCapacity == capacity) return;
      var replacement = new GpuEarthTilePool(gl, capacity);
      pool.Release();
      pool = replacement;
      RequestedCapacity = capacity;
    }

    internal void Draw(MapSourceManager source, EarthMapFrame frame)
    {
      pool.Sync(source);
      program.Bind(gl);
      vao.Bind(gl);
      gl.ActiveTexture(OpenGL.GL_TEXTURE0);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, builtinTexture);
      pool.Bind();
      SetCamera(source.Level, frame.Center, frame.Viewport, frame.Zoom, source.Projection);
      SetFloat("satLat", (float)frame.Satellite.LatitudeRad);
      SetFloat("satLon", (float)frame.Satellite.LongitudeRad);
      SetFloat("footprint", (float)frame.Footprint);
      SetFloat("brightness", frame.Brightness);
      gl.DrawArrays(OpenGL.GL_TRIANGLES, 0, 3);
      vao.Unbind(gl);
      program.Unbind(gl);
      gl.ActiveTexture(OpenGL.GL_TEXTURE0);
      if (gl.GetError() != 0) throw new InvalidOperationException("Earth tile shader render failed");
    }

    private void SetCamera(int level, GeoPoint center, Size viewport, double zoom, EarthTileProjection projection)
    {
      var (u, v) = EarthTileGrid.Coordinates(center, projection);
      int width = 1 << level, height = EarthTileGrid.Height(level, projection);
      double px = u * width, py = Math.Min(Math.BitDecrement((double)height), v * height);
      int x = (int)px, y = (int)py;
      SetInt("level", level);
      SetInt("projection", (int)projection);
      SetFloat("centerLat", (float)center.LatitudeRad);
      SetFloat("mercatorCenterLat", (float)(Math.Clamp(
        EarthTileGrid.SourcePoint(center, projection).Latitude,
        -EarthTileGrid.MercatorLimit, EarthTileGrid.MercatorLimit) * Geo.RinD));
      SetInt("originX", x);
      SetInt("originY", y);
      SetFloat("originU", (float)(px - x));
      SetFloat("originV", (float)(py - y));
      SetFloat("centerSinLat", (float)Math.Sin(center.LatitudeRad));
      SetFloat("centerCosLat", (float)Math.Cos(center.LatitudeRad));
      SetFloat("centerLon", (float)center.LongitudeRad);
      SetFloat("zoom", (float)zoom);
      SetFloat("aspect", (float)viewport.Width / viewport.Height);
    }

    private void SetInt(string name, int value) =>
      gl.Uniform1(program.GetUniformLocation(gl, name), value);

    private void SetFloat(string name, float value) =>
      program.SetUniform1(gl, name, value);
  }
}
