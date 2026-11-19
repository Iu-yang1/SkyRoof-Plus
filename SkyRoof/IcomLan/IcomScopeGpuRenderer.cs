using SharpGL;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;

namespace SkyRoof
{
  // GL resources are used only by the native presentation thread.
  internal sealed class IcomScopeGpuRenderer : IDisposable
  {
    private const int Points = IcomScopeWaterfallRing.Points;
    private const int Levels = 161;
    private readonly OpenGL gl;
    private readonly ShaderProgram shader = new();
    private readonly VertexBufferArray vao = new();
    private readonly VertexBuffer quad = new();
    private readonly uint[] textures = new uint[3];
    private readonly int[] rowUpload = new int[IcomScopeTextureTransfer.RowWords];
    private readonly int[] metadataRow = new int[4];
    private IcomScopeWaterfallRow[]? uploaded;
    private int[]? uploadedTable;
    private int[] metadata = [];
    private int locStart, locEnd, locHead, locRows, locWidth, locHeight, locLag;
    private readonly IcomScopeGpuPlotRenderer plot;

    internal IcomScopeGpuRenderer(OpenGL gl)
    {
      this.gl = gl;
      gl.Disable(OpenGL.GL_DEPTH_TEST);
      gl.Disable(OpenGL.GL_BLEND);
      shader.Create(gl, VertexShader, FragmentShader, null);
      shader.AssertValid(gl);
      shader.Bind(gl);
      locStart = shader.GetUniformLocation(gl, "uStart");
      locEnd = shader.GetUniformLocation(gl, "uEnd");
      locHead = shader.GetUniformLocation(gl, "uHead");
      locRows = shader.GetUniformLocation(gl, "uRows");
      locWidth = shader.GetUniformLocation(gl, "uWidth");
      locHeight = shader.GetUniformLocation(gl, "uHeight");
      locLag = shader.GetUniformLocation(gl, "uLag");
      gl.Uniform1(shader.GetUniformLocation(gl, "uRaw"), 0);
      gl.Uniform1(shader.GetUniformLocation(gl, "uMeta"), 1);
      gl.Uniform1(shader.GetUniformLocation(gl, "uColors"), 2);
      vao.Create(gl);
      vao.Bind(gl);
      quad.Create(gl);
      quad.Bind(gl);
      quad.SetData(gl, 0, new float[] { -1, -1, -1, 1, 1, -1, 1, 1 }, false, 2);
      vao.Unbind(gl);
      gl.GenTextures(3, textures);
      for (int i = 0; i < textures.Length; i++) ConfigureTexture(i);
      shader.Unbind(gl);
      plot = new IcomScopeGpuPlotRenderer(gl, vao, VertexShader);
      CheckError();
    }

    private void BindTexture(int index)
    {
      gl.ActiveTexture(OpenGL.GL_TEXTURE0 + (uint)index);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, textures[index]);
    }

    private void ConfigureTexture(int index)
    {
      BindTexture(index);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_NEAREST);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_NEAREST);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
    }

    internal void Draw(IcomScopeRenderSnapshot frame, IcomScopeGpuViewport viewport, double lag)
    {
      if (viewport.Scene is not { } scene) return;
      var state = IcomScopeGpuPlotState.FromSnapshot(frame, scene);
      gl.Viewport(0, 0, viewport.Width, viewport.Height);
      gl.ClearColor(5f / 255, 8f / 255, 14f / 255, 1);
      gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT);
      plot.Draw(frame, scene, viewport.Height, state);
      if (scene.Waterfall.Width > 1 && scene.Waterfall.Height > 1)
        DrawWaterfall(frame.Waterfall, scene.Waterfall, viewport.Height, state, lag);
      CheckError();
    }

    private void DrawWaterfall(IcomScopeWaterfallSnapshot snapshot, Rectangle rect,
      int height, IcomScopeGpuPlotState state, double lag)
    {
      if (snapshot.Rows.Length == 0) return;
      Upload(snapshot);
      gl.Viewport(rect.X, height - rect.Bottom, rect.Width, rect.Height);
      shader.Bind(gl);
      vao.Bind(gl);
      for (int i = 0; i < textures.Length; i++) BindTexture(i);
      gl.Uniform1(locStart, state.Start);
      gl.Uniform1(locEnd, state.End);
      gl.Uniform1(locHead, Math.Max(0, snapshot.Head));
      gl.Uniform1(locRows, snapshot.Rows.Length);
      gl.Uniform1(locWidth, rect.Width);
      gl.Uniform1(locHeight, rect.Height);
      gl.Uniform1(locLag, (float)lag);
      gl.DrawArrays(OpenGL.GL_TRIANGLE_STRIP, 0, 4);
      vao.Unbind(gl);
      shader.Unbind(gl);
    }

    private void Upload(IcomScopeWaterfallSnapshot snapshot)
    {
      if (!ReferenceEquals(uploadedTable, snapshot.ColorTable))
      {
        BindTexture(2);
        gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA8, Levels, Levels, 0,
          OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, snapshot.ColorTable);
        uploadedTable = snapshot.ColorTable;
      }
      if (ReferenceEquals(uploaded, snapshot.Rows)) return;
      if (uploaded == null || uploaded.Length != snapshot.Rows.Length)
        AllocateRaw(snapshot.Rows);
      else UpdateRaw(snapshot.Rows);
      UploadMetadata(snapshot.Rows);
      uploaded = snapshot.Rows;
    }

    private void AllocateRaw(IcomScopeWaterfallRow[] rows)
    {
      BindTexture(0);
      // GL_R8, padded to 476 bytes per row; bin 475 is never sampled.
      gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, 0x8229,
        IcomScopeTextureTransfer.RowBytes, rows.Length, 0,
        OpenGL.GL_RED, OpenGL.GL_UNSIGNED_BYTE, IcomScopeTextureTransfer.Pack(rows));
      BindTexture(1);
      gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, 0x8814, 1, rows.Length, 0,
        OpenGL.GL_RGBA, OpenGL.GL_FLOAT, IntPtr.Zero);
      metadata = new int[4 * rows.Length];
    }

    private void UpdateRaw(IcomScopeWaterfallRow[] rows)
    {
      BindTexture(0);
      for (int y = 0; y < rows.Length; y++)
      {
        if (uploaded != null && !IcomScopeTextureTransfer.RawChanged(uploaded[y], rows[y])) continue;
        IcomScopeTextureTransfer.PackRow(rows[y], rowUpload);
        gl.TexSubImage2D(OpenGL.GL_TEXTURE_2D, 0, 0, y, IcomScopeTextureTransfer.RowBytes, 1,
          OpenGL.GL_RED, OpenGL.GL_UNSIGNED_BYTE, rowUpload);
      }
    }

    private bool MetadataChangedAt(IcomScopeWaterfallRow[] rows, int y) =>
      uploaded == null || uploaded.Length != rows.Length ||
      IcomScopeTextureTransfer.MetadataChanged(uploaded[y], rows[y]);

    private void UploadMetadata(IcomScopeWaterfallRow[] rows)
    {
      int count = 0;
      for (int y = 0; y < rows.Length; y++)
        if (MetadataChangedAt(rows, y)) count++;
      if (count == 0) return;
      BindTexture(1);
      if (count > 8)
      {
        UploadAllMetadata(rows);
        return;
      }
      for (int y = 0; y < rows.Length; y++)
      {
        if (!MetadataChangedAt(rows, y)) continue;
        IcomScopeTextureTransfer.EncodeMetadata(rows[y], metadataRow);
        gl.TexSubImage2D(OpenGL.GL_TEXTURE_2D, 0, 0, y, 1, 1,
          OpenGL.GL_RGBA, OpenGL.GL_FLOAT, metadataRow);
      }
    }

    private void UploadAllMetadata(IcomScopeWaterfallRow[] rows)
    {
      int offset = 0;
      foreach (var row in rows)
      {
        IcomScopeTextureTransfer.EncodeMetadata(row, metadata.AsSpan(offset, 4));
        offset += 4;
      }
      gl.TexSubImage2D(OpenGL.GL_TEXTURE_2D, 0, 0, 0, 1, rows.Length,
        OpenGL.GL_RGBA, OpenGL.GL_FLOAT, metadata);
    }

    private void CheckError()
    {
      uint error = gl.GetError();
      if (error != 0) throw new InvalidOperationException($"OpenGL scope error 0x{error:X}");
    }

    public void Dispose()
    {
      plot.Dispose();
      gl.DeleteTextures(3, textures);
      // SharpGL 3.1.1 exposes no VertexBuffer.Delete; WGL context teardown
      // reclaims the buffer, while the VAO and program have explicit APIs.
      vao.Delete(gl);
      shader.Delete(gl);
    }

    private const string VertexShader = """
      #version 330 core
      layout(location=0) in vec2 pos;
      out vec2 uv;
      void main() {
        gl_Position = vec4(pos, 0, 1);
        uv = vec2(pos.x * 0.5 + 0.5, 0.5 - pos.y * 0.5);
      }
      """;

    private const string FragmentShader = """
      #version 330 core
      in vec2 uv;
      out vec4 color;
      uniform sampler2D uRaw, uMeta, uColors;
      uniform float uStart, uEnd, uLag;
      uniform int uHead, uRows, uWidth, uHeight;
      int levelAt(int src, int row, vec4 meta) {
        if (src < int(meta.y) || src >= int(meta.z)) return 0;
        return clamp(int(round(texelFetch(uRaw, ivec2(src,row),0).r * 255.0)), 0, 160);
      }
      vec4 mapped(int level, int floor) {
        return texelFetch(uColors, ivec2(level,clamp(floor,0,160)),0);
      }
      vec4 rowColor(int row, float x, float binsPerPixel) {
        vec4 meta = texelFetch(uMeta,ivec2(0,row),0);
        float source = x-meta.x;
        if (binsPerPixel > 1.0) {
          int first = max(0,int(ceil(source-binsPerPixel*0.5)));
          int last = min(474,int(floor(source+binsPerPixel*0.5)));
          int peak = 0;
          for (int i=first;i<=last;i++) peak=max(peak,levelAt(i,row,meta));
          return mapped(peak,int(meta.w));
        }
        int first = int(floor(source));
        return mix(mapped(levelAt(first,row,meta),int(meta.w)),
                   mapped(levelAt(first+1,row,meta),int(meta.w)),fract(source));
      }
      void main() {
        float xFraction = clamp((uv.x*float(uWidth)-0.5)/max(1.0,float(uWidth)-1.0),0.0,1.0);
        float x = clamp(mix(uStart,uEnd,xFraction) * 474.0,0.0,474.0);
        float binsPerPixel = (uEnd-uStart) * 474.0 / max(1.0,float(uWidth)-1.0);
        float step = float(uRows-1)/float(uHeight);
        float y = floor(uv.y*float(uHeight));
        float firstAge = clamp(uLag,0.0,1.0)+y*step;
        float endAge = firstAge+step;
        int first = clamp(int(floor(firstAge)),0,uRows-1);
        int last = clamp(int(ceil(endAge))-1,first,uRows-1);
        vec4 sum = vec4(0);
        for (int i=first;i<=last;i++) {
          float weight = max(0.0,min(endAge,float(i+1))-max(firstAge,float(i)));
          sum += weight*rowColor((uHead+i)%uRows,x,binsPerPixel);
        }
        color=sum/max(step,0.000001);
        color.a=1.0;
      }
      """;
  }
}
