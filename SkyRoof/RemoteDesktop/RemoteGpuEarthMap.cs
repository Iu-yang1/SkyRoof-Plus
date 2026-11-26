using System.Drawing.Imaging;
using SharpGL;
using SkyRoof.Remote.Ipc;
using VE3NEA;

namespace SkyRoof.RemoteDesktop;

// Reuse the production AEQD camera, GPU tile pool, map-source manager and shaders.
// Orbital data remains exclusively the Station's WGS84 ground-track result.
internal sealed class RemoteGpuEarthMap : UserControl
{
    private readonly OpenGLControl canvas;
    private readonly MapSourceManager sources;
    private readonly EarthViewCamera camera = new();
    private readonly uint[] texture = new uint[1];
    private EarthMapRenderer? renderer;
    private PolylineRenderer? lines;
    private SpriteRenderer? sprites;
    private Sprite? satellite, home;
    private GroundTrackResult? track;
    private GroundTrackPoint? current;
    private long utc;
    private Point? drag;
    private double zoom = 1;
    private bool failed;
    private bool footprint = true;
    private EarthMapSourceKind sourceKind;
    internal event Action<string>? MapStatus;
    internal event Action<string>? RendererFailed;

    internal RemoteGpuEarthMap(EarthMapSettings settings)
    {
        sources = new MapSourceManager(RequestDraw);
        canvas = new OpenGLControl();
        ((System.ComponentModel.ISupportInitialize)canvas).BeginInit();
        canvas.Dock = DockStyle.Fill;
        canvas.OpenGLVersion = SharpGL.Version.OpenGLVersion.OpenGL3_3;
        canvas.RenderContextType = RenderContextType.NativeWindow;
        canvas.RenderTrigger = RenderTrigger.Manual;
        canvas.DrawFPS = false;
        canvas.OpenGLInitialized += (_, _) => InitializeGraphics();
        canvas.OpenGLDraw += (_, _) => Draw();
        canvas.Resize += (_, _) => RequestDraw();
        canvas.MouseWheel += (_, e) =>
        {
            zoom = Math.Clamp(zoom * Math.Pow(1.15, e.Delta / 120.0), 1,
                Math.Max(1, EarthTileVisibility.MaximumZoom(canvas.ClientSize, sources.MaxLevel)));
            RequestDraw();
        };
        canvas.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { drag = e.Location; canvas.Capture = true; } };
        canvas.MouseMove += (_, e) =>
        {
            if (drag is not Point previous) return;
            drag = e.Location;
            if (camera.Pan(e.X - previous.X, e.Y - previous.Y, canvas.ClientSize, zoom)) RequestDraw();
        };
        canvas.MouseUp += (_, _) => { drag = null; canvas.Capture = false; };
        canvas.MouseCaptureChanged += (_, _) => { if (!canvas.Capture) drag = null; };
        canvas.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ResetView(); };
        Controls.Add(canvas);
        ((System.ComponentModel.ISupportInitialize)canvas).EndInit();
        Configure(settings);
    }

    internal bool ShowFootprint { get => footprint; set { footprint = value; RequestDraw(); } }
    internal void Configure(EarthMapSettings settings)
    {
        sourceKind = settings.Source;
        sources.Configure(settings);
        RequestDraw();
    }

    internal void SetTrack(GroundTrackResult value, long time, bool full)
    {
        if (full || track is null || track.NoradNumber != value.NoradNumber || track.CelestialTarget != value.CelestialTarget) track = value;
        utc = time;
        current = value.Points.MinBy(p => Math.Abs(p.UtcMilliseconds - time));
        camera.Follow(CenterTarget());
        RequestDraw();
    }

    private GeoPoint CenterTarget() => current is { } p
        ? new GeoPoint(p.LatitudeDegrees, p.LongitudeDegrees)
        : track is { } t ? new GeoPoint(t.SiteLatitudeDegrees, t.SiteLongitudeDegrees) : new GeoPoint(0, 0);

    internal void ResetView() { zoom = 1; camera.Reset(CenterTarget()); RequestDraw(); }

    private void RequestDraw()
    {
        if (IsDisposed || Disposing || !canvas.IsHandleCreated || failed) return;
        try
        {
            if (canvas.InvokeRequired) canvas.BeginInvoke(new Action(() => { if (!canvas.IsDisposed) canvas.Invalidate(); }));
            else canvas.Invalidate();
        }
        catch (InvalidOperationException) { /* A dock handle is being recreated. */ }
    }

    private void InitializeGraphics()
    {
        try
        {
            var gl = canvas.OpenGL;
            renderer = null;
            failed = false;
            gl.Disable(OpenGL.GL_DEPTH_TEST);
            gl.ActiveTexture(OpenGL.GL_TEXTURE0);
            gl.GenTextures(1, texture);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, texture[0]);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_REPEAT);
            using var bitmap = Utils.BitmapFromBytes(Properties.Resources.dxcc);
            var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try { gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGB, bitmap.Width, bitmap.Height, 0, OpenGL.GL_BGR, OpenGL.GL_UNSIGNED_BYTE, bits.Scan0); }
            finally { bitmap.UnlockBits(bits); }
            lines = new PolylineRenderer(gl);
            sprites = new SpriteRenderer(gl);
            satellite = new Sprite(gl, Properties.Resources.satellite);
            home = new Sprite(gl, Properties.Resources.ant);
            renderer = new EarthMapRenderer(gl, texture[0], sources.Capacity);
        }
        catch (Exception error) { Fail(error); }
    }

    private void Draw()
    {
        if (failed || renderer is null || lines is null || sprites is null || satellite is null || home is null) return;
        var size = canvas.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        try
        {
            var gl = canvas.OpenGL;
            gl.ClearColor(Theme.EarthSpace.R / 255f, Theme.EarthSpace.G / 255f, Theme.EarthSpace.B / 255f, 1);
            gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT);
            renderer.SetCapacity(sources.Capacity);
            sources.Update(camera.Center, size, zoom, renderer.Capacity);
            renderer.Draw(sources, new EarthMapFrame(camera.Center, CenterTarget(), Math.PI, size, zoom, Theme.EarthMapBrightness));
            MapStatus?.Invoke(sourceKind == EarthMapSourceKind.BuiltIn ? "Built-in GPU map" :
                $"{EarthMapProviders.ProviderName(sourceKind)} · {sources.Status} · L{sources.Level} · © provider");
            DrawOverlays(size);
        }
        catch (Exception error) { Fail(error); }
    }

    private void DrawOverlays(Size size)
    {
        if (track is not { } model || lines is null || sprites is null || satellite is null || home is null) return;
        DrawPath(model.Points.Where(p => p.UtcMilliseconds <= utc).Select(p => new GeoPoint(p.LatitudeDegrees, p.LongitudeDegrees)), size, Color.DeepSkyBlue);
        var future = model.Points.Where(p => p.UtcMilliseconds > utc).ToArray();
        for (int i = 0; i + 1 < future.Length; i += 2)
            DrawPath(new[] { new GeoPoint(future[i].LatitudeDegrees, future[i].LongitudeDegrees),
                new GeoPoint(future[i+1].LatitudeDegrees, future[i+1].LongitudeDegrees) }, size, Color.Cyan);
        home.Location = camera.Project(new GeoPoint(model.SiteLatitudeDegrees, model.SiteLongitudeDegrees), size, zoom);
        home.Enabled = (new GeoPoint(model.SiteLatitudeDegrees, model.SiteLongitudeDegrees) - camera.Center).DistanceRad < 0.9 * Math.PI;
        satellite.Enabled = current is not null && (CenterTarget() - camera.Center).DistanceRad < 0.9 * Math.PI;
        if (current is { } selected)
        {
            satellite.Location = camera.Project(CenterTarget(), size, zoom);
            if (footprint)
                DrawPath(EarthHorizon.Outline(selected.LatitudeDegrees, selected.LongitudeDegrees, selected.HeightMetres)
                    .Select(p => new GeoPoint(p.Item1, p.Item2)), size, Color.Gold);
        }
        sprites.DrawSprites([home, satellite]);
    }

    private void DrawPath(IEnumerable<GeoPoint> points, Size size, Color color)
    {
        var segment = new List<PointF>();
        foreach (var point in points)
        {
            if ((point - camera.Center).DistanceRad >= 0.9 * Math.PI)
            {
                if (segment.Count >= 2) lines?.DrawPolyline(segment.ToArray(), color, 2);
                segment.Clear();
            }
            else segment.Add(camera.Project(point, size, zoom));
        }
        if (segment.Count >= 2) lines?.DrawPolyline(segment.ToArray(), color, 2);
    }

    private void Fail(Exception error)
    {
        if (failed) return;
        failed = true;
        Serilog.Log.Warning("Remote GPU EarthView failed ({Type}); software map available.", error.GetType().Name);
        RendererFailed?.Invoke(error.GetType().Name);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) sources.Dispose();
        base.Dispose(disposing);
    }
}
