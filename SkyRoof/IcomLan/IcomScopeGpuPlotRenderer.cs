using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using SharpGL;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;

namespace SkyRoof
{
  internal sealed class IcomScopeGpuPlotRenderer : IDisposable
  {
    private const int Points = IcomScopeWaterfallRing.Points;
    private readonly OpenGL gl;
    private readonly VertexBufferArray vao;
    private readonly ShaderProgram shader = new();
    private readonly uint[] textures = new uint[3];
    private readonly int[] levels = new int[Points * 2];
    private readonly byte[] smooth = new byte[Points], smoothPeak = new byte[Points];
    private readonly Dictionary<string, int> uniforms = new();
    private long uploadedSequence = -1;
    private int uploadedSmoothing = -1;
    private string markerText = "", cursorKey = "";
    private Size markerSize, cursorSize;
    private Font? cursorFont;
    private int width, height;

    internal IcomScopeGpuPlotRenderer(OpenGL gl, VertexBufferArray vao, string vertex)
    {
      this.gl = gl;
      this.vao = vao;
      shader.Create(gl, vertex, FragmentShader, null);
      shader.AssertValid(gl);
      shader.Bind(gl);
      foreach (string key in new[] { "uTrace", "uMarkerText", "uCursorText", "uSize",
        "uZoom", "uPeak", "uMarker", "uBand", "uCursor", "uMarkerBox", "uCursorBox" })
        uniforms[key] = shader.GetUniformLocation(gl, key);
      gl.Uniform1(uniforms["uTrace"], 0);
      gl.Uniform1(uniforms["uMarkerText"], 1);
      gl.Uniform1(uniforms["uCursorText"], 2);
      gl.GenTextures(3, textures);
      for (int i = 0; i < 3; i++) Configure(i);
      shader.Unbind(gl);
    }

    private void Bind(int index)
    {
      gl.ActiveTexture(OpenGL.GL_TEXTURE0 + (uint)index);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, textures[index]);
    }

    private void Configure(int index)
    {
      Bind(index);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_NEAREST);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_NEAREST);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
      if (index != 0)
        gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA8, 1, 1, 0,
          OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, new int[1]);
    }

    internal void Draw(IcomScopeRenderSnapshot frame, IcomScopeGpuScene scene,
      int surfaceHeight, IcomScopeGpuPlotState state)
    {
      width = scene.Plot.Width;
      height = scene.Plot.Height;
      if (width < 2 || height < 2) return;
      UploadTrace(frame, scene.Options.Smoothing);
      UpdateLabels(state, scene.Options);
      gl.Viewport(scene.Plot.X, surfaceHeight - scene.Plot.Bottom, width, height);
      shader.Bind(gl);
      vao.Bind(gl);
      for (int i = 0; i < 3; i++) Bind(i);
      SetUniforms(scene, frame, state);
      gl.DrawArrays(OpenGL.GL_TRIANGLE_STRIP, 0, 4);
      vao.Unbind(gl);
      shader.Unbind(gl);
    }

    private void SetUniforms(IcomScopeGpuScene scene, IcomScopeRenderSnapshot frame, IcomScopeGpuPlotState state)
    {
      gl.Uniform2(uniforms["uSize"], (float)width, (float)height);
      gl.Uniform2(uniforms["uZoom"], state.Start, state.End);
      gl.Uniform1(uniforms["uPeak"], scene.Options.Peak && frame.PeakValid ? 1 : 0);
      gl.Uniform1(uniforms["uMarker"], state.Marker);
      gl.Uniform2(uniforms["uBand"], state.BandLeft, state.BandRight);
      gl.Uniform1(uniforms["uCursor"], state.CursorX);
      int mx = Math.Max(0, (int)Math.Round(state.Marker * (width - 1)) - 18);
      var marker = state.Marker < 0 ? Rectangle.Empty : new Rectangle(mx, 2, markerSize.Width, markerSize.Height);
      int cx = Math.Clamp(state.CursorX + 8, 3, Math.Max(3, width - cursorSize.Width - 1));
      var cursor = state.CursorX < 0 ? Rectangle.Empty : new Rectangle(cx, 22, cursorSize.Width, cursorSize.Height);
      Box("uMarkerBox", marker);
      Box("uCursorBox", cursor);
    }

    private void Box(string key, Rectangle rect) =>
      gl.Uniform4(uniforms[key], (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

    private void UploadTrace(IcomScopeRenderSnapshot frame, int smoothing)
    {
      if (uploadedSequence == frame.Sequence && uploadedSmoothing == smoothing) return;
      byte[] samples = frame.Samples, peak = frame.Peak;
      if (smoothing > 1)
      {
        IcomLanSpectrumView.SmoothSamplesInto(samples, smooth, smoothing);
        IcomLanSpectrumView.SmoothSamplesInto(peak, smoothPeak, smoothing);
        samples = smooth;
        peak = smoothPeak;
      }
      for (int i = 0; i < Points; i++)
      {
        levels[2 * i] = BitConverter.SingleToInt32Bits(IcomLanSpectrumView.MapSpectrumLevel(samples[i], frame.NoiseFloor));
        levels[2 * i + 1] = BitConverter.SingleToInt32Bits(IcomLanSpectrumView.MapSpectrumLevel(peak[i], frame.NoiseFloor));
      }
      Bind(0);
      // GL_RG32F / GL_RG: one raw display-level pair per original bin.
      if (uploadedSequence < 0)
        gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, 0x8230, Points, 1, 0, 0x8227, OpenGL.GL_FLOAT, levels);
      else
        gl.TexSubImage2D(OpenGL.GL_TEXTURE_2D, 0, 0, 0, Points, 1, 0x8227, OpenGL.GL_FLOAT, levels);
      uploadedSequence = frame.Sequence;
      uploadedSmoothing = smoothing;
    }

    private void UpdateLabels(IcomScopeGpuPlotState state, IcomScopeGpuOptions options)
    {
      if (markerText != state.MarkerText)
      {
        markerText = state.MarkerText;
        using var font = new Font("Segoe UI", 9f);
        markerSize = RenderLabel(1, markerText, font, true);
      }
      if (state.CursorX < 0) return;
      string key = state.CursorText + "|" + options.FontName + "|" + options.FontSize + "|" + options.FontStyle;
      if (cursorKey == key) return;
      if (cursorFont == null || cursorFont.Name != options.FontName ||
          cursorFont.Size != options.FontSize || cursorFont.Style != options.FontStyle)
      {
        cursorFont?.Dispose();
        cursorFont = new Font(options.FontName, options.FontSize, options.FontStyle);
      }
      cursorKey = key;
      cursorSize = RenderLabel(2, state.CursorText, cursorFont, false);
    }

    private Size RenderLabel(int texture, string value, Font font, bool marker)
    {
      Size size;
      using (var measure = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
      using (var graphics = Graphics.FromImage(measure))
      {
        var measured = graphics.MeasureString(value, font);
        size = marker ? new Size(36, font.Height + 4)
          : new Size((int)Math.Ceiling(measured.Width) + 8, (int)Math.Ceiling(measured.Height) + 6);
      }
      using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
      using (var graphics = Graphics.FromImage(bitmap))
      {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(marker ? Color.Transparent : Color.FromArgb(243, 20, 27, 36));
        using var brush = new SolidBrush(marker ? Color.FromArgb(255, 200, 204) : Color.FromArgb(224, 230, 235));
        if (marker)
        {
          using var format = new StringFormat { Alignment = StringAlignment.Center };
          graphics.DrawString(value, font, brush, new RectangleF(0, 0, size.Width, size.Height), format);
        }
        else
        {
          using var border = new Pen(Color.FromArgb(105, 125, 138));
          graphics.DrawRectangle(border, 0, 0, size.Width - 1, size.Height - 1);
          graphics.DrawString(value, font, brush, 4, 3);
        }
      }
      UploadLabel(texture, bitmap);
      return size;
    }

    private void UploadLabel(int texture, Bitmap bitmap)
    {
      var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
      var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try
      {
        var pixels = new int[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
          Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width);
        Bind(texture);
        gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA8, bitmap.Width, bitmap.Height, 0,
          OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, pixels);
      }
      finally { bitmap.UnlockBits(data); }
    }

    public void Dispose()
    {
      cursorFont?.Dispose();
      gl.DeleteTextures(3, textures);
      shader.Delete(gl);
    }

    private const string FragmentShader = """
      #version 330 core
      in vec2 uv;
      out vec4 outputColor;
      uniform sampler2D uTrace, uMarkerText, uCursorText;
      uniform vec2 uSize, uZoom, uBand;
      uniform int uPeak, uCursor;
      uniform float uMarker;
      uniform vec4 uMarkerBox, uCursorBox;
      vec2 sampleBin(int i) { return texelFetch(uTrace,ivec2(clamp(i,0,474),0),0).rg; }
      float yValue(float v) { return uSize.y-2.0-v/160.0*max(1.0,uSize.y-4.0); }
      float segmentDist(vec2 p,vec2 a,vec2 b) {
        vec2 d=b-a;
        vec2 q=p-a-d*clamp(dot(p-a,d)/max(dot(d,d),0.00001),0.0,1.0);
        return dot(q,q);
      }
      float xValue(int i) { return (float(i)/474.0-uZoom.x)/max(0.000001,uZoom.y-uZoom.x)*(uSize.x-1.0); }
      float traceDist(vec2 p,int bin,int ch) {
        float result=10000.0;
        int first=max(0,bin-1);
        vec2 a=vec2(xValue(first),yValue(sampleBin(first)[ch]));
        for(int i=first; i<=min(473,bin+1);i++) {
          vec2 b=vec2(xValue(i+1),yValue(sampleBin(i+1)[ch]));
          if(p.y>=min(a.y,b.y)-2.0 && p.y<=max(a.y,b.y)+2.0)
            result=min(result,segmentDist(p,a,b));
          a=b;
        }
        return sqrt(result);
      }
      vec3 label(vec3 base, sampler2D tex, vec4 box, vec2 p) {
        vec2 q=p-box.xy;
        if(any(lessThan(q,vec2(0)))||any(greaterThanEqual(q,box.zw))) return base;
        vec4 value=texelFetch(tex,ivec2(q),0);
        return mix(base,value.rgb,value.a);
      }
      void main() {
        vec2 p=clamp(uv*uSize-vec2(0.5),vec2(0),uSize-vec2(1));
        vec3 c=vec3(5,8,14)/255.0;
        if(p.x>=uBand.x && p.x<uBand.y) c=mix(c,vec3(235,68,76)/255.0,48.0/255.0);
        float intervals=uSize.x>=900.0?5.0:uSize.x>=600.0?4.0:3.0;
        float gx=(uSize.x-1.0)/intervals;
        if(abs(p.x-round(p.x/gx)*gx)<0.55) c=vec3(59,75,87)/255.0;
        float gy=uSize.y/4.0;
        if(abs(p.y-round(p.y/gy)*gy)<0.55) c=vec3(36,48,59)/255.0;
        float x=clamp(mix(uZoom.x,uZoom.y,p.x/max(1.0,uSize.x-1.0))*474.0,0.0,474.0);
        float binWidth=(uZoom.y-uZoom.x)*474.0/max(1.0,uSize.x-1.0);
        float a,b;
        if(binWidth>1.0) {
          vec2 peak=vec2(0);
          int lo=clamp(int(ceil(x-binWidth/2.0)),0,474);
          int hi=clamp(int(floor(x+binWidth/2.0)),lo,474);
          for(int i=lo;i<=hi;i++) peak=max(peak,sampleBin(i));
          a=abs(p.y-yValue(peak.r)); b=abs(p.y-yValue(peak.g));
        } else {
          a=traceDist(p,int(floor(x)),0);
          b=uPeak!=0?traceDist(p,int(floor(x)),1):1000.0;
        }
        c=mix(c,vec3(44,219,88)/255.0,1.0-smoothstep(0.3,1.3,a));
        if(uPeak!=0 && mod(p.x,4.0)<2.0)
          c=mix(c,vec3(255,203,74)/255.0,1.0-smoothstep(0.2,1.1,b));
        if(uMarker>=0.0 && abs(p.x-uMarker*(uSize.x-1.0))<0.8) c=vec3(235,68,76)/255.0;
        if(uCursor>=0 && abs(p.x-float(uCursor))<0.6 && mod(p.y,7.0)<4.0) c=vec3(186,211,226)/255.0;
        c=label(c,uMarkerText,uMarkerBox,p);
        c=label(c,uCursorText,uCursorBox,p);
        outputColor=vec4(c,1);
      }
      """;
  }
}
