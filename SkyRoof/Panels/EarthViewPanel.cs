using System.Text;
using SharpGL.VertexBuffers;
using SharpGL;
using WeifenLuo.WinFormsUI.Docking;
using SharpGL.Shaders;
using VE3NEA;
using System.Diagnostics;
using System.Drawing.Imaging;
using Serilog;
using static System.Windows.Forms.AxHost;

namespace SkyRoof
{
  public partial class EarthViewPanel : DockContent
  {
    private readonly Context ctx;
    private OpenGL gl;
    private ShaderProgram ShaderProgram;
    private VertexBufferArray VertexBufferArray;
    private uint[] TextureIds = new uint[1];
    private int VertexCount;
    private SpriteRenderer SpriteRenderer;
    private Sprite SatelliteSprite, HomeSprite, NorthSprite, SouthSprite;
    private PolylineRenderer PolylineRenderer;
    private double SatFootprint;
    private readonly EarthViewCamera camera = new();
    private MapSourceManager? mapSources;
    private EarthMapRenderer? onlineRenderer;
    private bool onlineRendererFailed;
    private bool refreshingMapSelector;
    private Point? dragPosition;
    private GeoPoint nextSatGeoPoint;
    private bool satellitePositionValid;

    public GeoPoint Home, SatGeoPoint;
    public GeoPoint Center => camera.Center;
    public double Zoom = 1;
    public float Brightness = Theme.EarthMapBrightness; // the fragment shader scales the map by this
    public SatnogsDbSatellite Satellite;

    public enum EarthViewMode { RealTime, Pass }
    private EarthViewMode Mode = EarthViewMode.RealTime;
    private SatellitePass? Pass;
    private List<GeoPoint>? CoveragePolygon;
    private long polygonRequestVersion;
    private readonly Dictionary<SatellitePass, List<GeoPoint>> polygonCache = new();
    private long positionRevision;
    private bool positionUpdatePending;
    private bool zoomAfterSatelliteSwitch;
    private readonly Color PolygonColor = Color.FromArgb(230, 0, 110, 255);


    public EarthViewPanel() { InitializeComponent(); }

    public EarthViewPanel(Context ctx)
    {
      this.ctx = ctx;
      Log.Information("Creating EarthViewPanel");
      InitializeComponent();

      ctx.EarthViewPanel = this;
      ctx.MainForm.EarthViewMNU.Checked = true;

      mapSources = new MapSourceManager(RequestMapRedraw);
      Disposed += (_, _) => mapSources?.Dispose();
      MapSourceCombo.SelectedIndexChanged += MapSourceChanged;
      UiLocalizationService.Bind(this, RefreshMapSourceOptions);
      ApplyMapSettings();
      SetGridSquare();
      SetSatellite();

      openglControl1.MouseWheel += OpenglControl1_MouseWheel;
      openglControl1.MouseDown += EarthMouseDown;
      openglControl1.MouseMove += EarthMouseMove;
      openglControl1.MouseUp += EarthMouseUp;
      openglControl1.MouseCaptureChanged += (_, _) =>
      {
        if (!openglControl1.Capture) EndEarthDrag();
      };
      openglControl1.MouseDoubleClick += (_, e) =>
      {
        if (e.Button == MouseButtons.Left) ResetView();
      };
      ResetViewButton.Click += (_, _) => ResetView();
    }

    private void RequestMapRedraw()
    {
      if (IsDisposed || Disposing || !IsHandleCreated) return;
      try
      {
        BeginInvoke((Action)(() =>
        {
          if (!IsDisposed && !Disposing) openglControl1.Invalidate();
        }));
      }
      catch (InvalidOperationException) { /* Dock handle was recreated. */ }
    }

    private void RefreshMapSourceOptions()
    {
      if (ctx == null) return;
      refreshingMapSelector = true;
      try
      {
        MapSourceCombo.Items.Clear();
        MapSourceCombo.Items.AddRange(new object[]
        {
          SettingsLanguage.Text("内置地图", "Built-in map"),
          SettingsLanguage.Text("天地图 · 矢量", "Tianditu · Vector"),
          SettingsLanguage.Text("天地图 · 影像", "Tianditu · Imagery"),
          SettingsLanguage.Text("天地图 · 地形", "Tianditu · Terrain")
        });
        MapSourceCombo.SelectedIndex = Enum.IsDefined(ctx.Settings.EarthMap.Source)
          ? (int)ctx.Settings.EarthMap.Source : 0;
        RefreshMapStatus();
      }
      finally { refreshingMapSelector = false; }
    }

    private void MapSourceChanged(object? sender, EventArgs e)
    {
      if (refreshingMapSelector || MapSourceCombo.SelectedIndex < 0) return;
      ctx.Settings.EarthMap.Source = (EarthMapSourceKind)MapSourceCombo.SelectedIndex;
      ApplyMapSettings();
    }

    internal void ApplyMapSettings()
    {
      mapSources?.Configure(ctx.Settings.EarthMap);
      onlineRendererFailed = false;
      RefreshMapSourceOptions();
      openglControl1.Invalidate();
    }

    private void RefreshMapStatus()
    {
      if (mapSources == null) return;
      string status = onlineRendererFailed ? SettingsLanguage.Text("GPU 瓦片渲染不可用，内置地图", "GPU tiles unavailable; built-in map") :
        mapSources.Status switch
        {
          EarthMapStatus.Offline => SettingsLanguage.Text("内置地图", "Built-in map"),
          EarthMapStatus.KeyMissing => SettingsLanguage.Text("请在设置中填写天地图 API Key", "Configure Tianditu API Key in Settings"),
          EarthMapStatus.Unavailable => SettingsLanguage.Text("天地图不可用，内置地图回退", "Tianditu unavailable; built-in fallback"),
          EarthMapStatus.Ready => SettingsLanguage.Text("天地图", "Tianditu") + $" · L{mapSources.Level}",
          EarthMapStatus.Partial => SettingsLanguage.Text("天地图 · 部分瓦片", "Tianditu · partial tiles"),
          _ => SettingsLanguage.Text("天地图 · 加载中", "Tianditu · loading")
        };
      if (ctx.Settings.EarthMap.Source != EarthMapSourceKind.BuiltIn)
        status += " | © 天地图 / Tianditu";
      if (MapStatusLabel.Text != status) MapStatusLabel.Text = status;
    }

    public void SetGridSquare()
    {
      Home = GridSquare.ToGeoPoint(ctx.Settings.User.Square);
      camera.Follow(DefaultCenter());
    }

    private void EarthViewPanel_FormClosing(object sender, FormClosingEventArgs e)
    {
      Log.Information("Closing EarthViewPanel");
      ctx.EarthViewPanel = null;
      ctx.MainForm.EarthViewMNU.Checked = false;
    }

    private void openglControl1_Resize(object sender, EventArgs e)
    {
      openglControl1.Invalidate();
      ValidateZoom();
    }

    private void OpenglControl1_MouseWheel(object? sender, MouseEventArgs e)
    {
      Zoom *= Math.Pow(1.1, e.Delta / 120.0);
      zoomAfterSatelliteSwitch = false;
      ValidateZoom();
      openglControl1.Invalidate();
    }

    private void EarthMouseDown(object? sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;
      dragPosition = e.Location;
      openglControl1.Capture = true;
      openglControl1.Cursor = Cursors.SizeAll;
    }

    private void EarthMouseMove(object? sender, MouseEventArgs e)
    {
      if (dragPosition is not Point previous) return;
      dragPosition = e.Location;
      if (!camera.Pan(e.X - previous.X, e.Y - previous.Y,
          openglControl1.ClientSize, Zoom)) return;
      zoomAfterSatelliteSwitch = false;
      openglControl1.Invalidate();
    }

    private void EarthMouseUp(object? sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Left) EndEarthDrag();
    }

    private void EndEarthDrag()
    {
      dragPosition = null;
      openglControl1.Cursor = Cursors.Default;
      if (openglControl1.Capture) openglControl1.Capture = false;
    }

    private GeoPoint DefaultCenter() => Mode == EarthViewMode.RealTime &&
      satellitePositionValid ? SatGeoPoint : Home;

    private void ResetView()
    {
      EndEarthDrag();
      camera.Reset(DefaultCenter());
      openglControl1.Invalidate();
    }

    private void ValidateZoom()
    {
      var diam = Math.Min(openglControl1.ClientSize.Width, openglControl1.ClientSize.Height);
      if (diam > 0)
      {
        int level = mapSources?.MaxLevel ?? 18;
        double maximum = EarthTileVisibility.MaximumZoom(openglControl1.ClientSize, level);
        Zoom = Math.Clamp(Zoom, 1, maximum);
      }
    }

    internal void SetSatellite(SatnogsDbSatellite? satellite = null)
    {
      Satellite = satellite ?? ctx.SatelliteSelector.SelectedSatellite;
      label1.Text = Satellite.name;
      ++positionRevision;
      zoomAfterSatelliteSwitch = !camera.IsManual;
      // The constructor may call this before WinForms has a synchronization
      // context. Use the existing synchronous path only before the handle
      // exists, then keep every later selection free of Orekit propagation.
      if (!IsHandleCreated)
      {
        ComputeSatLocation();
        if (zoomAfterSatelliteSwitch) Zoom = 0.9 * Math.PI / SatFootprint;
        ValidateZoom();
        zoomAfterSatelliteSwitch = false;
      }
      else
      {
        satellitePositionValid = false;
        SatFootprint = Math.PI;
        SatGeoPoint = nextSatGeoPoint = Home;
        camera.Follow(DefaultCenter());
        SchedulePositionUpdate();
      }
      openglControl1.Invalidate();
    }
    public void Advance()
    {
      if (Satellite == null) return;

      // on AOS, switch from pass mode to real time
      if (Mode == EarthViewMode.Pass && Pass != null && Pass.IsActive())
        RealTimeRadioBtn.Checked = true;

      SchedulePositionUpdate();
      openglControl1.Invalidate();
    }

    internal void SetPass(SatellitePass? pass)
    {
      Pass = pass;
      long request = ++polygonRequestVersion;
      CoveragePolygon = pass != null && polygonCache.TryGetValue(pass, out var cached)
        ? cached : null;
      if (pass != null && CoveragePolygon == null)
        ComputeCoveragePolygonAsync(pass, request).DoNotAwait();

      PassRadioBtn.Enabled = pass != null;
      PassRadioBtn.Text = pass == null
        ? "Selected Pass"
        : pass.HasOrbitNumber
          ? $"Orbit #{pass.OrbitNumber} of {pass.Satellite.name}"
          : $"{pass.Satellite.name} visibility window";

      // real time mode if the pass is currently active, otherwise pass mode for the upcoming pass
      if (pass != null && !pass.IsActive())
        PassRadioBtn.Checked = true;
      else
        RealTimeRadioBtn.Checked = true;

      SchedulePositionUpdate();

      // zoom so the whole coverage polygon fits the rectangular panel
      ZoomToFitPolygon();

      openglControl1.Invalidate();
    }

    private async Task ComputeCoveragePolygonAsync(SatellitePass pass, long request)
    {
      try
      {
        await Task.Delay(60);
        if (IsDisposed || Disposing || request != polygonRequestVersion ||
            !ReferenceEquals(Pass, pass))
          return;
        var polygon = await Task.Run(pass.GetCoveragePolygon);
        if (IsDisposed || Disposing || request != polygonRequestVersion ||
            !ReferenceEquals(Pass, pass))
          return;
        if (polygonCache.Count > 48)
          polygonCache.Clear();
        polygonCache[pass] = polygon;
        CoveragePolygon = polygon;
        ZoomToFitPolygon();
        openglControl1.Invalidate();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Satellite coverage polygon generation failed.");
      }
    }

    // set the zoom so that every polygon vertex fits within the rectangular panel.
    // the projection scales linearly with Zoom, so we project at Zoom = 1 and fit the
    // x and y extents independently against the [-1, 1] clip space.
    private void ZoomToFitPolygon()
    {
      if (camera.IsManual || CoveragePolygon == null || CoveragePolygon.Count == 0) return;

      var size = openglControl1.ClientSize;
      if (size.Width == 0 || size.Height == 0) return;
      double diam = Math.Min(size.Width, size.Height);

      double maxX = 0, maxY = 0;
      foreach (var g in CoveragePolygon)
      {
        var path = g - Center;
        double ro = path.DistanceRad / Math.PI;
        double phi = Geo.HalfPi - path.AzimuthRad;
        double x = Math.Abs(ro * Math.Cos(phi) * diam / size.Width);
        double y = Math.Abs(ro * Math.Sin(phi) * diam / size.Height);
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
      }

      double maxXY = Math.Max(maxX, maxY);
      if (maxXY > 1e-6) Zoom = 0.9 / maxXY;
      ValidateZoom();
    }

    private void radioButton_CheckedChanged(object sender, EventArgs e)
    {
      var radioBtn = (RadioButton)sender;
      if (!radioBtn.Checked) return;

      Mode = PassRadioBtn.Checked ? EarthViewMode.Pass : EarthViewMode.RealTime;
      camera.Follow(DefaultCenter());

      SchedulePositionUpdate();
      openglControl1.Invalidate();
    }

    private void SchedulePositionUpdate()
    {
      if (positionUpdatePending || IsDisposed || Disposing) return;
      if (!IsHandleCreated)
      {
        ComputeSatLocation();
        return;
      }
      positionUpdatePending = true;
      UpdatePositionAsync(Satellite, positionRevision).DoNotAwait();
    }

    private async Task UpdatePositionAsync(SatnogsDbSatellite sat, long revision)
    {
      try
      {
        var tracker = sat.Tracker;
        var position = await Task.Run(() =>
        {
          var now = DateTime.UtcNow;
          var current = tracker.Predict(now);
          var next = tracker.Predict(now.AddSeconds(10));
          if (current == null || next == null)
            return (Valid: false, Latitude: 0d, Longitude: 0d,
              NextLatitude: 0d, NextLongitude: 0d, Footprint: Math.PI);
          return (Valid: true, Latitude: current.Latitude.Degrees,
            Longitude: current.Longitude.Degrees,
            NextLatitude: next.Latitude.Degrees,
            NextLongitude: next.Longitude.Degrees,
            Footprint: current.GetFootprintAngle().Radians);
        });
        if (IsDisposed || Disposing || revision != positionRevision ||
            !ReferenceEquals(sat, Satellite) ||
            !ReferenceEquals(tracker, sat.Tracker))
          return;

        ApplySatLocation(position.Valid, position.Latitude, position.Longitude,
          position.NextLatitude, position.NextLongitude, position.Footprint);
        if (zoomAfterSatelliteSwitch)
        {
          if (!camera.IsManual) Zoom = 0.9 * Math.PI / SatFootprint;
          ValidateZoom();
          zoomAfterSatelliteSwitch = false;
        }
        openglControl1.Invalidate();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Background Earth view satellite position update failed.");
      }
      finally
      {
        positionUpdatePending = false;
        if (!IsDisposed && !Disposing && revision != positionRevision)
          SchedulePositionUpdate();
      }
    }

    private void ComputeSatLocation()
    {
      var now = DateTime.UtcNow;
      var p = Satellite.Tracker.Predict(now);
      var nextP = Satellite.Tracker.Predict(now.AddSeconds(10));
      ApplySatLocation(p != null && nextP != null,
        p?.Latitude.Degrees ?? 0, p?.Longitude.Degrees ?? 0,
        nextP?.Latitude.Degrees ?? 0, nextP?.Longitude.Degrees ?? 0,
        p?.GetFootprintAngle().Radians ?? Math.PI);
    }

    private void ApplySatLocation(
      bool valid, double latitude, double longitude,
      double nextLatitude, double nextLongitude, double footprint)
    {
      satellitePositionValid = valid;
      SatGeoPoint = valid ? new GeoPoint(latitude, longitude) : Home;
      nextSatGeoPoint = valid ? new GeoPoint(nextLatitude, nextLongitude) : Home;
      SatFootprint = valid ? footprint : Math.PI;
      camera.Follow(DefaultCenter());
    }
    




    //----------------------------------------------------------------------------------------------
    //                                 init opengl objects
    //----------------------------------------------------------------------------------------------
    private void openglControl1_OpenGLInitialized(object sender, EventArgs e)
    {
      gl = openglControl1.OpenGL;
      // The docking library may recreate the native GL context. CPU tiles
      // remain available; new texture names and the index are rebuilt lazily.
      onlineRenderer = null;
      onlineRendererFailed = false;
      CheckError(false);

      SpriteRenderer = new(gl);
      CheckError(false);
      PolylineRenderer = new(gl);
      CheckError(false);
      SatelliteSprite = new Sprite(gl, Properties.Resources.satellite);
      HomeSprite = new Sprite(gl, Properties.Resources.ant);
      NorthSprite = new Sprite(gl, Properties.Resources.N);
      SouthSprite = new Sprite(gl, Properties.Resources.S);

      gl.Disable(OpenGL.GL_DEPTH_TEST);
      CheckError();

      LoadShaders();
      CreateVba();
      LoadTexture();

      gl.Uniform1(ShaderProgram.GetUniformLocation(gl, "worldMapTexture"), 0);
      CheckError();
    }


    private void LoadShaders()
    {
      ShaderProgram = new ShaderProgram();
      CheckError();

      ShaderProgram.Create(gl,
        Encoding.ASCII.GetString(Properties.Resources.EarthVertexShader),
        Encoding.ASCII.GetString(Properties.Resources.EarthFragmentShader),
        null);
      CheckError();

      ShaderProgram.AssertValid(gl);
      CheckError();

      ShaderProgram.Bind(gl);
      CheckError();
    }


    private const int PHI_STEPS = 192;
    private const int RO_STEPS = 96;
    private const float TwoPi = (float)(2 * Math.PI);

    private void CreateVba()
    {
      CheckError(false);

      VertexBufferArray = new VertexBufferArray();
      CheckError();
      VertexBufferArray.Create(gl);
      CheckError();
      VertexBufferArray.Bind(gl);
      CheckError();

      // generate a grid of polar coordinates
      VertexCount = RO_STEPS * (PHI_STEPS + 1) * 2;
      var data = new float[VertexCount * 2];
      int idx = 0;

      for (int ro = 0; ro < RO_STEPS; ro++)
        for (int phi = 0; phi <= PHI_STEPS; phi++)
        {
          data[idx++] = ro / (float)RO_STEPS;
          data[idx++] = phi * TwoPi / PHI_STEPS;
          data[idx++] = (ro + 1) / (float)RO_STEPS;
          data[idx++] = phi * TwoPi / PHI_STEPS;
        }

      var dataBuffer = new VertexBuffer();
      CheckError();
      dataBuffer.Create(gl);
      CheckError();
      dataBuffer.Bind(gl);
      CheckError();
      dataBuffer.SetData(gl, 0, data, false, 2);
      CheckError();

      VertexBufferArray.Unbind(gl);
      CheckError();
    }

    private void LoadTexture()
    {
      CheckError(false);

      gl.GenTextures(1, TextureIds);
      CheckError();

      gl.BindTexture(OpenGL.GL_TEXTURE_2D, TextureIds[0]);
      CheckError(false); // produces false positives

      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR);    // shrink
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);    // stretch
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE); // lat
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_REPEAT);        // lon


      //using (Bitmap bitmap = new Bitmap(@"C:\Users\Alex\Desktop\sat\maps\worldMapTexture.bmp"))
      //using (Bitmap bitmap = new Bitmap(Properties.Resources.NaturalEarth))
      //using (Bitmap bitmap = new Bitmap(Properties.Resources.dxcc))
      using (Bitmap bitmap = Utils.BitmapFromBytes(Properties.Resources.dxcc))
      {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
          ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGB, bitmap.Width, bitmap.Height, 0,
          OpenGL.GL_BGR, OpenGL.GL_UNSIGNED_BYTE, data.Scan0);

        bitmap.UnlockBits(data);
      }
      CheckError();
    }




    //----------------------------------------------------------------------------------------------
    //                                        draw
    //----------------------------------------------------------------------------------------------
    private void openglControl1_OpenGLDraw(object sender, SharpGL.RenderEventArgs args)
    {
      var size = openglControl1.ClientSize;
      if (size.Width <= 0 || size.Height <= 0) return;
      var gl = openglControl1.OpenGL;
      CheckError(false);
      gl.ClearColor(Theme.EarthSpace.R / 255f, Theme.EarthSpace.G / 255f, Theme.EarthSpace.B / 255f, 1);
      gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT);
      if (!TryDrawOnlineMap(size))
      {
        gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT);
        DrawBuiltInMap();
      }
      SetSpriteAttributes();
      SpriteRenderer.DrawSprites([SatelliteSprite, HomeSprite, NorthSprite, SouthSprite]);
      CheckError();
      DrawCoveragePolygon();
    }

    private bool TryDrawOnlineMap(Size size)
    {
      if (mapSources == null || ctx.Settings.EarthMap.Source == EarthMapSourceKind.BuiltIn ||
          onlineRendererFailed || string.IsNullOrWhiteSpace(ctx.Settings.EarthMap.TiandituKey))
      {
        RefreshMapStatus();
        return false;
      }
      try
      {
        onlineRenderer ??= new EarthMapRenderer(gl, TextureIds[0], mapSources.Capacity);
        onlineRenderer.SetCapacity(mapSources.Capacity);
        mapSources.Update(Center, size, Zoom, onlineRenderer.Capacity);
        RefreshMapStatus();
        if (mapSources.ReadyCount == 0) return false;
        onlineRenderer.Draw(mapSources, new EarthMapFrame(Center, SatGeoPoint,
          Mode == EarthViewMode.RealTime ? SatFootprint : Math.PI, size, Zoom, Brightness));
        return true;
      }
      catch (Exception ex)
      {
        onlineRendererFailed = true;
        Log.Warning("Earth map GPU renderer unavailable ({Type}); using built-in map.", ex.GetType().Name);
        gl.ActiveTexture(OpenGL.GL_TEXTURE0);
        RefreshMapStatus();
        return false;
      }
    }

    private void DrawBuiltInMap()
    {
      ShaderProgram.Bind(gl);
      CheckError();

      gl.ActiveTexture(OpenGL.GL_TEXTURE0);
      CheckError();
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, TextureIds[0]);
      CheckError();

      VertexBufferArray.Bind(gl);
      CheckError();

      ShaderProgram.SetUniform1(gl, "in_homeLat", (float)Center.LatitudeRad);
      CheckError();
      ShaderProgram.SetUniform1(gl, "in_homeLon", (float)Center.LongitudeRad);
      CheckError();
      ShaderProgram.SetUniform1(gl, "in_satLat", (float)SatGeoPoint.LatitudeRad);
      CheckError();
      ShaderProgram.SetUniform1(gl, "in_satLon", (float)SatGeoPoint.LongitudeRad);
      CheckError();
      ShaderProgram.SetUniform1(gl, "in_footprint",
        (float)(Mode == EarthViewMode.RealTime ? SatFootprint : Math.PI));
      CheckError();
      ShaderProgram.SetUniform1(gl, "in_aspect", openglControl1.ClientSize.Width / (float)openglControl1.ClientSize.Height);
      CheckError();
      ShaderProgram.SetUniform1(gl, "in_zoom", (float)Zoom);
      CheckError();
      ShaderProgram.SetUniform1(gl, "in_brightness", Brightness);
      CheckError();

      gl.DrawArrays(OpenGL.GL_TRIANGLE_STRIP, 0, VertexCount);
      CheckError();

      VertexBufferArray.Unbind(gl);
      CheckError();
      ShaderProgram.Unbind(gl);
      CheckError();
    }

    private void DrawCoveragePolygon()
    {
      if (CoveragePolygon == null || CoveragePolygon.Count < 2) return;

      // in real time mode, only show the polygon while the pass is in progress
      if (Mode == EarthViewMode.RealTime && (Pass == null || !Pass.IsActive())) return;

      // Break the contour at the azimuthal projection's antipodal seam.
      // Keep all remaining visible sections instead of hiding the whole pass.
      var segment = new List<PointF>();
      void Flush()
      {
        if (segment.Count >= 2)
          PolylineRenderer.DrawPolyline(segment.ToArray(), PolygonColor, 3f);
        segment.Clear();
      }
      foreach (var g in CoveragePolygon)
      {
        if ((g - Center).DistanceRad > 0.9 * Math.PI) Flush();
        else segment.Add(ComputeLocation(g));
      }
      Flush();
      CheckError();
    }

    private void SetSpriteAttributes()
    {
      // control size, pixels
      var size = openglControl1.ClientSize;
      // diameter of the inscribed circle, pixels
      var diam = Math.Min(size.Width, size.Height);

      // scale sprites to look nice at all control sizes
      var scale = 0.6f + diam * 0.0007f;
      SatelliteSprite.Scale = scale;
      HomeSprite.Scale = scale;
      NorthSprite.Scale = 0.4f * scale;
      SouthSprite.Scale = 0.4f * scale;

      // The satellite may be off-centre after panning. Use its projected
      // heading rather than the bearing at the projection centre.
      SatelliteSprite.Enabled = Mode == EarthViewMode.RealTime && satellitePositionValid;
      SatelliteSprite.Angle = camera.Heading(SatGeoPoint, nextSatGeoPoint, size, Zoom);
      SatelliteSprite.Location = ComputeLocation(SatGeoPoint);

      // home location in the azimuthal projection
      HomeSprite.Location = ComputeLocation(Home);

      // N and S at the poles
      NorthSprite.Location = ComputeLocation(GeoPoint.NorthPole);
      SouthSprite.Location = ComputeLocation(GeoPoint.SouthPole);
    }

    private PointF ComputeLocation(GeoPoint point) =>
      camera.Project(point, openglControl1.ClientSize, Zoom);



    //----------------------------------------------------------------------------------------------
    //                                   error handling
    //----------------------------------------------------------------------------------------------
    private void CheckError(bool log = true)
    {
      ExceptionLogger.CheckOpenglError(gl, log);
    }
  }
}
