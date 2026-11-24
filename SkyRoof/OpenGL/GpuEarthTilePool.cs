using SharpGL;

namespace SkyRoof
{
  // Same open-addressed integer hash table in C# and GLSL. The table is
  // sparse even when the active service has millions of possible tiles.
  internal static class EarthTileIndex
  {
    internal const int Size = 2048;

    internal static uint Hash(EarthTileKey key) => unchecked(
      (uint)key.X * 73856093u ^ (uint)key.Y * 19349663u ^
      (uint)(key.Level * 2 + (key.Labels ? 1 : 0)) * 83492791u);

    internal static int[] Build(IEnumerable<KeyValuePair<EarthTileKey, int>> entries)
    {
      var table = new int[Size * 4];
      foreach (var (key, slot) in entries)
      {
        int offset = (int)(Hash(key) & (Size - 1));
        while (table[offset * 4 + 3] != 0) offset = (offset + 1) & (Size - 1);
        table[offset * 4] = key.X;
        table[offset * 4 + 1] = key.Y;
        table[offset * 4 + 2] = key.Level * 2 + (key.Labels ? 1 : 0);
        table[offset * 4 + 3] = slot + 1;
      }
      return table;
    }
  }

  internal sealed class GpuEarthTilePool
  {
    private const uint TextureArray = 0x8C1A;
    private const uint MaxArrayLayers = 0x88FF;
    private const int Rgba8 = 0x8058;
    private const uint Rgba32I = 0x8D82;
    private const uint RgbaInteger = 0x8D99;
    private readonly OpenGL gl;
    private readonly uint[] textures = new uint[2];
    private readonly Dictionary<EarthTileKey, int> resident = new();
    private readonly Queue<int> free = new();
    private int revision = -1;

    internal int Capacity { get; private set; }

    internal GpuEarthTilePool(OpenGL gl, int requested)
    {
      this.gl = gl;
      var maximum = new int[1];
      gl.GetInteger(MaxArrayLayers, maximum);
      Capacity = Math.Min(requested, maximum[0]);
      if (Capacity < 2) throw new NotSupportedException("OpenGL texture arrays unavailable");
      gl.GenTextures(2, textures);
      try { Allocate(); }
      catch
      {
        gl.DeleteTextures(2, textures);
        throw;
      }
    }

    private void Allocate()
    {
      gl.BindTexture(TextureArray, textures[0]);
      SetParameters(TextureArray, OpenGL.GL_LINEAR);
      gl.TexImage3D(TextureArray, 0, Rgba8, 256, 256, Capacity, 0,
        OpenGL.GL_RGBA, OpenGL.GL_UNSIGNED_BYTE, IntPtr.Zero);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, textures[1]);
      SetParameters(OpenGL.GL_TEXTURE_2D, OpenGL.GL_NEAREST);
      gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, Rgba32I, EarthTileIndex.Size, 1, 0,
        RgbaInteger, OpenGL.GL_INT, new int[EarthTileIndex.Size * 4]);
      if (gl.GetError() != 0) throw new InvalidOperationException("GPU tile allocation failed");
      for (int i = 0; i < Capacity; i++) free.Enqueue(i);
    }

    private void SetParameters(uint target, uint filter)
    {
      gl.TexParameter(target, OpenGL.GL_TEXTURE_MIN_FILTER, (int)filter);
      gl.TexParameter(target, OpenGL.GL_TEXTURE_MAG_FILTER, (int)filter);
      gl.TexParameter(target, OpenGL.GL_TEXTURE_WRAP_S, (int)OpenGL.GL_CLAMP_TO_EDGE);
      gl.TexParameter(target, OpenGL.GL_TEXTURE_WRAP_T, (int)OpenGL.GL_CLAMP_TO_EDGE);
    }

    internal void Sync(MapSourceManager source)
    {
      if (revision == source.Revision) return;
      var tiles = source.Tiles;
      foreach (var key in resident.Keys.Where(k => !tiles.ContainsKey(k)).ToArray())
      {
        free.Enqueue(resident[key]);
        resident.Remove(key);
      }
      gl.ActiveTexture(OpenGL.GL_TEXTURE0 + 1);
      gl.BindTexture(TextureArray, textures[0]);
      foreach (var (key, pixels) in tiles)
      {
        if (resident.ContainsKey(key) || free.Count == 0) continue;
        int slot = free.Dequeue();
        Upload(slot, pixels);
        resident.Add(key, slot);
      }
      gl.ActiveTexture(OpenGL.GL_TEXTURE0 + 2);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, textures[1]);
      gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, Rgba32I, EarthTileIndex.Size, 1, 0,
        RgbaInteger, OpenGL.GL_INT, EarthTileIndex.Build(resident));
      revision = source.Revision;
      if (gl.GetError() != 0) throw new InvalidOperationException("GPU tile upload failed");
    }

    private unsafe void Upload(int slot, byte[] pixels)
    {
      fixed (byte* pointer = pixels)
        gl.TexSubImage3D(TextureArray, 0, 0, 0, slot, 256, 256, 1,
          OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, (IntPtr)pointer);
    }

    internal void Bind()
    {
      gl.ActiveTexture(OpenGL.GL_TEXTURE0 + 1);
      gl.BindTexture(TextureArray, textures[0]);
      gl.ActiveTexture(OpenGL.GL_TEXTURE0 + 2);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, textures[1]);
      gl.ActiveTexture(OpenGL.GL_TEXTURE0);
    }

    internal void Release() => gl.DeleteTextures(2, textures);
  }
}
