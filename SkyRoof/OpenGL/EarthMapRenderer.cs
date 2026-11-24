using SharpGL;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;
using VE3NEA;

namespace SkyRoof
{
  internal sealed class EarthMapRenderer
  {
    private readonly OpenGL gl;
    private readonly ShaderProgram program = new();
    private readonly VertexBufferArray vao = new();
    private GpuEarthTilePool pool;

    internal int Capacity => pool.Capacity;
    internal int RequestedCapacity { get; private set; }

    internal EarthMapRenderer(OpenGL gl, int capacity)
    {
      this.gl = gl;
      RequestedCapacity = capacity;
      vao.Create(gl);
      program.Create(gl, EarthMapShaders.Vertex, EarthMapShaders.Fragment, null);
      program.Bind(gl);
      SetInt("worldMapTexture", 0);
      SetInt("tileTexture", 1);
      SetInt("tileIndex", 2);
      program.AssertValid(gl);
      program.Unbind(gl);
      pool = new GpuEarthTilePool(gl, capacity);
    }

    internal void SetCapacity(int capacity)
    {
      if (RequestedCapacity == capacity) return;
      var replacement = new GpuEarthTilePool(gl, capacity);
      pool.Release();
      pool = replacement;
      RequestedCapacity = capacity;
    }

    internal void Draw(MapSourceManager source, uint builtinTexture, GeoPoint center,
      GeoPoint satellite, double footprint, Size viewport, double zoom, float brightness)
    {
      pool.Sync(source);
      program.Bind(gl);
      vao.Bind(gl);
      gl.ActiveTexture(OpenGL.GL_TEXTURE0);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, builtinTexture);
      pool.Bind();
      SetCamera(source.Level, center, viewport, zoom);
      SetFloat("satLat", (float)satellite.LatitudeRad);
      SetFloat("satLon", (float)satellite.LongitudeRad);
      SetFloat("footprint", (float)footprint);
      SetFloat("brightness", brightness);
      gl.DrawArrays(OpenGL.GL_TRIANGLES, 0, 3);
      VertexBufferArray.Unbind(gl);
      program.Unbind(gl);
      gl.ActiveTexture(OpenGL.GL_TEXTURE0);
      if (gl.GetError() != 0) throw new InvalidOperationException("Earth tile shader render failed");
    }

    private void SetCamera(int level, GeoPoint center, Size viewport, double zoom)
    {
      var (u, v) = EarthTileVisibility.Coordinates(center);
      int width = 1 << level, height = width / 2;
      double px = u * width, py = Math.Min(Math.BitDecrement((double)height), v * height);
      int x = (int)px, y = (int)py;
      SetInt("level", level);
      SetInt("originX", x);
      SetInt("originY", y);
      SetFloat("originU", (float)(px - x));
      SetFloat("originV", (float)(py - y));
      SetFloat("centerLat", (float)center.LatitudeRad);
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
