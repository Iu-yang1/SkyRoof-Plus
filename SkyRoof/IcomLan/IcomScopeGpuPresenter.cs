using SharpGL;
using SharpGL.Version;

namespace SkyRoof
{
  internal sealed record IcomScopeGpuViewport(int Width, int Height, float Start, float End);

  internal sealed class IcomScopeGpuPresenter : IDisposable
  {
    private readonly IntPtr window;
    private readonly Action<string?> status;
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread thread;
    private IcomScopeRenderSnapshot? latest;
    private IcomScopeGpuViewport viewport = new(0, 0, 0, 1);
    private bool stopped, started;
    private int minimumGeneration;
    private long presentCount;
    private string renderer = "starting";

    internal IcomScopeGpuPresenter(IntPtr window, Action<string?> status)
    {
      this.window = window;
      this.status = status;
      thread = new Thread(Run) { Name = "Icom Scope GPU Present", IsBackground = true };
    }

    internal long PresentCount => Interlocked.Read(ref presentCount);
    internal string Renderer => Volatile.Read(ref renderer);

    internal void Start()
    {
      lock (gate)
      {
        if (stopped || started) return;
        started = true;
        thread.Start();
      }
    }

    internal void Offer(IcomScopeRenderSnapshot value)
    {
      lock (gate)
      {
        if (stopped || value.Generation < minimumGeneration ||
            (latest != null && value.Sequence <= latest.Sequence)) return;
        latest = value;
        wake.Set();
      }
    }

    internal void Clear(int generation)
    {
      lock (gate)
      {
        if (stopped) return;
        minimumGeneration = generation;
        if (latest != null && latest.Generation < generation) latest = null;
        wake.Set();
      }
    }

    internal void SetViewport(IcomScopeGpuViewport value)
    {
      lock (gate)
      {
        if (stopped || viewport == value) return;
        viewport = value;
        wake.Set();
      }
    }

    internal void Refresh()
    {
      lock (gate)
        if (!stopped) wake.Set();
    }

    private void Run()
    {
      var gl = new OpenGL();
      IcomScopeGpuRenderer? drawing = null;
      bool ready = false;
      try
      {
        if (!gl.Create(OpenGLVersion.OpenGL3_3, RenderContextType.NativeWindow, 1, 1, 32, window))
          throw new InvalidOperationException("OpenGL native context creation failed");
        gl.MakeCurrent();
        Volatile.Write(ref renderer, gl.GetString(OpenGL.GL_RENDERER) ?? "unknown renderer");
        drawing = new IcomScopeGpuRenderer(gl);
        while (true)
        {
          wake.WaitOne();
          IcomScopeRenderSnapshot? frame;
          IcomScopeGpuViewport size;
          lock (gate)
          {
            if (stopped) return;
            frame = latest;
            size = viewport;
          }
          if (size.Width < 2 || size.Height < 2) continue;
          if (frame == null)
          {
            gl.Viewport(0, 0, size.Width, size.Height);
            gl.ClearColor(0.02f, 0.03f, 0.055f, 1f);
            gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT);
            gl.Blit(IntPtr.Zero);
            continue;
          }
          drawing.Draw(frame.Waterfall, size);
          gl.Blit(IntPtr.Zero);
          Interlocked.Increment(ref presentCount);
          if (ready) continue;
          ready = true;
          status(null);
        }
      }
      catch (Exception ex)
      {
        Serilog.Log.Warning(ex, "Icom Scope GPU fallback");
        status(ex.Message);
      }
      finally
      {
        try
        {
          drawing?.Dispose();
          gl.RenderContextProvider?.Destroy();
        }
        catch (Exception ex)
        {
          Serilog.Log.Warning(ex, "Icom Scope GPU context teardown");
        }
        lock (gate)
        {
          stopped = true;
          wake.Dispose();
        }
      }
    }

    public void Dispose()
    {
      bool join;
      lock (gate)
      {
        join = started;
        if (!stopped)
        {
          stopped = true;
          if (started) wake.Set();
          else wake.Dispose();
        }
      }
      // HWND destruction must happen after the presenting thread releases
      // its WGL context. No join is used during live drawing.
      if (join && Thread.CurrentThread != thread) thread.Join();
    }
  }
}
