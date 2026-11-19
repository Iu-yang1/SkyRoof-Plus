namespace SkyRoof
{
  // Dedicated native HWND. WinForms owns its placement and input routing;
  // the presenting thread owns the GL context and never touches a Control.
  internal sealed class IcomScopeGpuSurface : Control
  {
    private readonly Action<IcomScopeGpuSurface, string?> status;
    private volatile IcomScopeGpuPresenter? presenter;
    private IcomScopeRenderSnapshot? latest;
    private double start, end = 1;

    internal IcomScopeGpuSurface(Action<IcomScopeGpuSurface, string?> status)
    {
      this.status = status;
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
        ControlStyles.Opaque, true);
      SetStyle(ControlStyles.Selectable, false);
      TabStop = false;
    }

    protected override CreateParams CreateParams
    {
      get
      {
        var parameters = base.CreateParams;
        parameters.ClassStyle |= 0x20; // CS_OWNDC: stable device context for WGL.
        return parameters;
      }
    }

    internal long PresentCount => presenter?.PresentCount ?? 0;
    internal string Renderer => presenter?.Renderer ?? "starting";

    internal void Offer(IcomScopeRenderSnapshot snapshot)
    {
      IcomScopeRenderSnapshot? current;
      do
      {
        current = Volatile.Read(ref latest);
        if (current != null && snapshot.Sequence <= current.Sequence) return;
      }
      while (!ReferenceEquals(Interlocked.CompareExchange(ref latest, snapshot, current), current));
      presenter?.Offer(snapshot);
    }

    internal void SetZoom(double first, double last)
    {
      start = first;
      end = last;
      UpdateViewport();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
      base.OnHandleCreated(e);
      var next = new IcomScopeGpuPresenter(Handle, error => status(this, error));
      presenter = next;
      UpdateViewport();
      if (Volatile.Read(ref latest) is { } snapshot) next.Offer(snapshot);
      next.Start();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
      var previous = presenter;
      presenter = null;
      previous?.Dispose();
      base.OnHandleDestroyed(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
      base.OnSizeChanged(e);
      UpdateViewport();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
      base.OnVisibleChanged(e);
      UpdateViewport();
    }

    // Expose windows must redraw even when there is no fresh sweep.
    protected override void OnPaint(PaintEventArgs e) => presenter?.Refresh();
    protected override void OnPaintBackground(PaintEventArgs e) { }

    private void UpdateViewport()
    {
      var size = Visible ? ClientSize : Size.Empty;
      presenter?.SetViewport(new(size.Width, size.Height, (float)start, (float)end));
    }

    protected override void WndProc(ref Message m)
    {
      if (m.Msg == 0x84) // WM_NCHITTEST; hand tuning/zoom back to the parent.
      {
        m.Result = new IntPtr(-1); // HTTRANSPARENT (same UI thread).
        return;
      }
      base.WndProc(ref m);
    }
  }
}
