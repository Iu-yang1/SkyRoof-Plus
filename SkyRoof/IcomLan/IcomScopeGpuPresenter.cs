using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpGL;
using SharpGL.Version;

namespace SkyRoof
{
  internal sealed record IcomScopeGpuViewport(int Width, int Height, IcomScopeGpuScene? Scene);

  internal sealed record IcomScopeGpuPerf(long FreshCount, double FreshFps, IcomScopePaintMetrics.Summary Timing)
  {
    internal static readonly IcomScopeGpuPerf Empty = new(0, 0, default);
  }

  internal sealed class IcomScopeGpuPresenter : IDisposable
  {
    private readonly IntPtr window;
    private readonly Action<string?> status;
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread thread;
    private IcomScopeRenderSnapshot? latest;
    private IcomScopeGpuViewport viewport = new(0, 0, null);
    private bool stopped, started;
    private int minimumGeneration;
    private long presentCount;
    private readonly IcomScopePaintMetrics metrics = new();
    private IcomScopeGpuPerf perf = IcomScopeGpuPerf.Empty;
    private long lastPerfTick, lastFreshCount;
    internal IcomScopeGpuPerf Perf => Volatile.Read(ref perf);
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
        IntPtr context = WglGetCurrentContext();
        IntPtr dc = WglGetCurrentDC();
        VerifyCurrentContext(context, dc);
        // Only the back buffer is cleared/drawn; the front is retained until
        // all chart and waterfall commands are submitted.
        gl.DrawBuffer(OpenGL.GL_BACK);
        Volatile.Write(ref renderer, gl.GetString(OpenGL.GL_RENDERER) ?? "unknown renderer");
        drawing = new IcomScopeGpuRenderer(gl);
        var clock = new IcomScopeWaterfallClock();
        bool animate = false;
        long nextDraw = 0;
        while (true)
        {
          int delay = DelayUntil(nextDraw);
          wake.WaitOne(animate ? delay : Timeout.Infinite);
          delay = DelayUntil(nextDraw);
          if (delay > 0) Thread.Sleep(delay);
          IcomScopeRenderSnapshot? frame;
          IcomScopeGpuViewport size;
          lock (gate)
          {
            if (stopped) return;
            frame = latest;
            size = viewport;
          }
          animate = false;
          if (size.Width < 2 || size.Height < 2 || size.Scene == null) continue;
          nextDraw = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 60;
          gl.MakeCurrent();
          VerifyCurrentContext(context, dc);
          if (frame == null)
          {
            gl.Viewport(0, 0, size.Width, size.Height);
            gl.ClearColor(0.02f, 0.03f, 0.055f, 1f);
            gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT);
            SwapCurrentBuffers(context, dc);
            continue;
          }
          long startTick = Stopwatch.GetTimestamp();
          double lag = clock.Lag(frame.Waterfall, startTick, size.Scene.Held);
          animate = !size.Scene.Held && size.Scene.Waterfall.Height > 1 && lag > 0;
          drawing.Draw(frame, size, lag);
          SwapCurrentBuffers(context, dc);
          Interlocked.Increment(ref presentCount);
          RecordPresent(frame, startTick);
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
          gl.MakeNothingCurrent();
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

    private static int DelayUntil(long due) => (int)Math.Max(0,
      Math.Ceiling((due - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency));

    [DllImport("opengl32.dll", EntryPoint = "wglGetCurrentContext")]
    private static extern IntPtr WglGetCurrentContext();

    [DllImport("opengl32.dll", EntryPoint = "wglGetCurrentDC")]
    private static extern IntPtr WglGetCurrentDC();

    [DllImport("gdi32.dll", EntryPoint = "SwapBuffers", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SwapBuffers(IntPtr dc);

    private static void VerifyCurrentContext(IntPtr context, IntPtr dc)
    {
      if (context == IntPtr.Zero || dc == IntPtr.Zero ||
          WglGetCurrentContext() != context || WglGetCurrentDC() != dc)
        throw new InvalidOperationException("OpenGL scope context/DC changed before presentation");
    }

    private static void SwapCurrentBuffers(IntPtr context, IntPtr dc)
    {
      VerifyCurrentContext(context, dc);
      if (!SwapBuffers(dc))
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "OpenGL SwapBuffers failed");
    }

    private void RecordPresent(IcomScopeRenderSnapshot frame, long startTick)
    {
      long now = Stopwatch.GetTimestamp();
      metrics.Record(now, now - startTick, frame.Frame?.TimestampUtc.Ticks ?? 0, DateTime.UtcNow.Ticks);
      if (lastPerfTick == 0)
      {
        lastPerfTick = now;
        lastFreshCount = metrics.FreshPaintCount;
        return;
      }
      double elapsed = (now - lastPerfTick) / (double)Stopwatch.Frequency;
      if (elapsed < 0.5) return;
      long fresh = metrics.FreshPaintCount;
      Volatile.Write(ref perf, new IcomScopeGpuPerf(fresh, (fresh - lastFreshCount) / elapsed, metrics.Snapshot()));
      lastFreshCount = fresh;
      lastPerfTick = now;
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
