using Serilog;
using SGPdotNET.Observation;
using SGPdotNET.CoordinateSystem;
using SGPdotNET.Util;
using VE3NEA;
using VE3NEA.Clock;
using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof
{
  public partial class QsoSchedulerPanel : DockContent
  {
    internal class OverlapPass
    {
      public SatnogsDbSatellite Satellite;
      public SatellitePass MyPass;
      public SatellitePass DxPass;
      public string MySquare = "";
      public string DxSquare = "";
      public DateTime CommonStart;
      public DateTime CommonEnd;
      public double MyMaxElevation;
      public double DxMaxElevation;
      public PointF[] MyMiniPath = Array.Empty<PointF>();
      public PointF[] DxMiniPath = Array.Empty<PointF>();
      public bool Geostationary;
      public int OrbitNumber;
    }

    private readonly Context ctx = null!;
    private bool changing;
    private List<ListViewItem> Items = new();
    private CancellationTokenSource? predictionCancellation;
    private bool canceledByInputChange;
    private string statusKey = "QsoScheduler.Ready";
    private object?[] statusArgs = [];

    internal bool IsPredictionRunning => predictionCancellation != null;

    private readonly record struct PredictionRequest(
      SatnogsDbSatellite Satellite, string MySquare, double MyAltitudeMeters,
      string DxSquare, DateTime StartUtc, DateTime EndUtc);

    private readonly record struct PredictionProgress(int Percent, string StageKey);

    internal static bool HasValidRange(DateTime startUtc, DateTime endUtc) =>
      endUtc > startUtc;

    internal static bool TryCommonWindow(
      DateTime myStart, DateTime myEnd, DateTime dxStart, DateTime dxEnd,
      DateTime rangeStart, DateTime rangeEnd, out DateTime start, out DateTime end)
    {
      start = new[] { myStart, dxStart, rangeStart }.Max();
      end = new[] { myEnd, dxEnd, rangeEnd }.Min();
      return end - start >= TimeSpan.FromSeconds(30);
    }

    private readonly Font BoldFont;
    private readonly Pen PathPen = new Pen(Theme.Now, 2);

    public QsoSchedulerPanel()
    {
      InitializeComponent();
      InitializeTimeRange();
    }

    public QsoSchedulerPanel(Context ctx)
    {
      Log.Information("Creating QsoScheduler");
      this.ctx = ctx;
      InitializeComponent();
      InitializeTimeRange();

      ctx.QsoSchedulerPanel = this;
      ctx.MainForm.QsoSchedulerMNU.Checked = true;

      BoldFont = new Font(PredictionList.Font, FontStyle.Bold);

      int rowHeight = TextRenderer.MeasureText("0", Font, Size, TextFormatFlags.NoPadding).Height * 2 + 15;
      PredictionList.SetRowHeight(rowHeight);
      PredictionList.SetTooltipDelay(1500);

      SetSatelliteList();
    }

    private void InitializeTimeRange()
    {
      // DateTimePicker is a wall-clock editor: explicitly interpret both
      // displayed values as UTC, never as the Windows local time zone.
      var utc = DateTime.UtcNow;
      var minute = new DateTime(utc.Year, utc.Month, utc.Day,
        utc.Hour, utc.Minute, 0, DateTimeKind.Unspecified);
      changing = true;
      StartUtcPicker.Value = minute;
      EndUtcPicker.Value = minute.AddDays(14);
      changing = false;
      CalculationProgress.Visible = false;
      SetStatus("QsoScheduler.Ready");
      RefreshActionState();
    }

    private void QsoScheduler_FormClosing(object sender, FormClosingEventArgs e)
    {
      predictionCancellation?.Cancel();
      Log.Information("Closing QsoScheduler");
      ctx.QsoSchedulerPanel = null;
      ctx.MainForm.QsoSchedulerMNU.Checked = false;
    }

    private void DxSquareEdit_TextChanged(object sender, EventArgs e)
    {
      bool ok = GridSquare.IsValid(DxSquareEdit.Text.Trim());
      DxSquareEdit.BackColor = ok ? SystemColors.Window : Color.FromArgb(0xFF, 0xDD, 0xDD);

      if (!changing) UpdatePredictions();
    }

    private void SatelliteComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
      if (!changing) UpdatePredictions();
    }

    private void RangePicker_ValueChanged(object sender, EventArgs e)
    {
      if (!changing) UpdatePredictions();
    }

    private void CalculateButton_Click(object sender, EventArgs e)
    {
      if (predictionCancellation != null)
      {
        predictionCancellation.Cancel();
        canceledByInputChange = false;
        SetStatus("QsoScheduler.Cancelling");
        RefreshActionState();
        return;
      }

      if (!TryCreateRequest(out var request))
      {
        RefreshActionState();
        return;
      }

      var cancellation = new CancellationTokenSource();
      predictionCancellation = cancellation;
      canceledByInputChange = false;
      CalculationProgress.Value = 0;
      CalculationProgress.Visible = true;
      SetStatus("QsoScheduler.ProgressMe", 0);
      RefreshActionState();
      _ = RunPredictionAsync(request, cancellation);
    }

    private void PredictionList_Resize(object sender, EventArgs e)
    {
      PredictionList.Columns[0].Width = PredictionList.ClientSize.Width;
    }

    private void PredictionList_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
      e.Item = Items[e.ItemIndex];
    }

    private void PredictionList_MouseDown(object sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;
      var item = PredictionList.GetItemAt(e.X, e.Y);
      if (item == null) return;

      using var popup = new QsoScheduleForm((OverlapPass)item.Tag!);
      popup.ShowDialog(this);
    }

    internal void RefreshLocalizedUi()
    {
      // Relabel only. A locale change must never start or cancel an orbit search.
      RefreshActionState();
      RenderStatus();
      PredictionList.Invalidate();
    }

    public void SetSatelliteList()
    {
      string? selectedId = (SatelliteComboBox.SelectedItem as SatnogsDbSatellite)?.sat_id;
      changing = true;
      try
      {
        SatelliteComboBox.Items.Clear();
        SatelliteComboBox.Items.AddRange(ctx.SatelliteSelector.GroupSatellites);
        if (selectedId != null)
          for (int i = 0; i < SatelliteComboBox.Items.Count; i++)
            if ((SatelliteComboBox.Items[i] as SatnogsDbSatellite)?.sat_id == selectedId)
            {
              SatelliteComboBox.SelectedIndex = i;
              break;
            }
      }
      finally { changing = false; }
      UpdatePredictions();
    }

    // Called by text/date/satellite edits and external orbit/site refreshes.
    // Never perform an orbit prediction from a UI event.
    public void UpdatePredictions()
    {
      if (predictionCancellation != null)
      {
        canceledByInputChange = true;
        predictionCancellation.Cancel();
        SetStatus("QsoScheduler.Cancelling");
      }
      else
      {
        SetStatus("QsoScheduler.InputChanged");
      }

      Items.Clear();
      PredictionList.VirtualListSize = 0;
      PredictionList.Invalidate();
      RefreshActionState();
    }

    private bool TryCreateRequest(out PredictionRequest request)
    {
      request = default;
      var satellite = SatelliteComboBox.SelectedItem as SatnogsDbSatellite;
      if (satellite?.Tracker?.Enabled != true || ctx == null ||
          !GridSquare.IsValid(DxSquareEdit.Text.Trim()) ||
          !GridSquare.IsValid(ctx.Settings.User.Square))
        return false;

      DateTime startUtc = DateTime.SpecifyKind(StartUtcPicker.Value, DateTimeKind.Utc);
      DateTime endUtc = DateTime.SpecifyKind(EndUtcPicker.Value, DateTimeKind.Utc);
      if (!HasValidRange(startUtc, endUtc)) return false;

      request = new PredictionRequest(satellite, ctx.Settings.User.Square,
        ctx.Settings.User.Altitude, DxSquareEdit.Text.Trim(), startUtc, endUtc);
      return true;
    }

    private void SetStatus(string key, params object?[] args)
    {
      statusKey = key;
      statusArgs = args;
      RenderStatus();
    }

    private void RenderStatus() =>
      StatusLabel.Text = UiLocalizationService.Format(statusKey, statusArgs);

    private void RefreshActionState()
    {
      bool running = predictionCancellation != null;
      CalculateButton.Text = UiLocalizationService.Get(running
        ? "QsoScheduler.Cancel" : "QsoScheduler.Calculate");
      CalculateButton.Enabled = running
        ? !predictionCancellation!.IsCancellationRequested
        : TryCreateRequest(out _);
    }

    private async Task RunPredictionAsync(PredictionRequest request,
      CancellationTokenSource cancellation)
    {
      try
      {
        // Progress<T> captures the WinForms synchronization context. No
        // ListView, Label or ProgressBar is ever accessed by the worker.
        var progress = new Progress<PredictionProgress>(step =>
        {
          if (IsDisposed || Disposing || cancellation.IsCancellationRequested ||
              !ReferenceEquals(predictionCancellation, cancellation)) return;
          CalculationProgress.Value = Math.Clamp(step.Percent, 0, 100);
          SetStatus(step.StageKey, step.Percent);
        });

        var overlaps = await Task.Run(
          () => ComputePredictions(request, progress, cancellation.Token),
          cancellation.Token);
        cancellation.Token.ThrowIfCancellationRequested();
        if (IsDisposed || Disposing) return;

        // Publish one immutable completed result on the UI thread. Old
        // rows never expose partially computed paths to owner-draw.
        Items = overlaps.Select(pass => new ListViewItem { Tag = pass }).ToList();
        PredictionList.VirtualListSize = Items.Count;
        PredictionList.Invalidate();
        CalculationProgress.Value = 100;
        SetStatus("QsoScheduler.Completed", Items.Count);
      }
      catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
      {
        if (!IsDisposed && !Disposing)
          SetStatus(canceledByInputChange
            ? "QsoScheduler.InputChanged" : "QsoScheduler.Cancelled");
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Pass matching prediction failed.");
        if (!IsDisposed && !Disposing)
          SetStatus("QsoScheduler.Failed", ex.Message);
      }
      finally
      {
        if (ReferenceEquals(predictionCancellation, cancellation))
        {
          predictionCancellation = null;
          if (!IsDisposed && !Disposing)
          {
            CalculationProgress.Visible = false;
            RefreshActionState();
          }
        }
        cancellation.Dispose();
      }
    }

    private static List<OverlapPass> ComputePredictions(PredictionRequest request,
      IProgress<PredictionProgress> progress, CancellationToken cancellation)
    {
      cancellation.ThrowIfCancellationRequested();
      var satellite = request.Satellite;
      var tracker = satellite.Tracker;
      var myStation = MakeStation(request.MySquare, request.MyAltitudeMeters);
      var dxStation = MakeStation(request.DxSquare, 0);

      // A geostationary target must cover the requested interval, not a
      // synthetic 24-hour interval beginning at DateTime.UtcNow.
      List<SatelliteVisibilityPeriod> VisiblePasses(GroundStation station)
      {
        if (!tracker.IsGeoStationary())
          return tracker.ComputePasses(station, request.StartUtc, request.EndUtc);
        var observation = tracker.Observe(station, request.StartUtc);
        if (observation == null || observation.Elevation.Degrees < 0)
          return new List<SatelliteVisibilityPeriod>();
        return new List<SatelliteVisibilityPeriod>
        {
          new(null!, request.StartUtc, request.EndUtc, observation.Elevation,
            request.StartUtc + (request.EndUtc - request.StartUtc) / 2,
            station.Location)
        };
      }

      bool geo = tracker.IsGeoStationary();
      progress.Report(new PredictionProgress(5, "QsoScheduler.ProgressMe"));
      var myVis = VisiblePasses(myStation);
      cancellation.ThrowIfCancellationRequested();
      progress.Report(new PredictionProgress(40, "QsoScheduler.ProgressDx"));
      var dxVis = VisiblePasses(dxStation);
      cancellation.ThrowIfCancellationRequested();
      progress.Report(new PredictionProgress(70, "QsoScheduler.ProgressOverlap"));

      var myPasses = myVis.Select(p => new SatellitePass(myStation, satellite, p))
        .OrderBy(p => p.StartTime).ToList();
      var dxPasses = dxVis.Select(p => new SatellitePass(dxStation, satellite, p))
        .OrderBy(p => p.StartTime).ToList();

      var overlaps = new List<OverlapPass>();
      int dxFirst = 0;
      for (int i = 0; i < myPasses.Count; i++)
      {
        cancellation.ThrowIfCancellationRequested();
        var my = myPasses[i];
        while (dxFirst < dxPasses.Count &&
               dxPasses[dxFirst].EndTime <= my.StartTime)
          dxFirst++;

        // Sorted intervals: only DX passes intersecting this local pass
        // need examination, rather than the former O(me * DX) scan.
        for (int j = dxFirst; j < dxPasses.Count &&
             dxPasses[j].StartTime < my.EndTime; j++)
        {
          cancellation.ThrowIfCancellationRequested();
          var dx = dxPasses[j];
          if (!TryCommonWindow(my.StartTime, my.EndTime, dx.StartTime, dx.EndTime,
                request.StartUtc, request.EndUtc, out var start, out var end))
            continue;
          var overlap = new OverlapPass
          {
            Satellite = satellite,
            MyPass = my,
            DxPass = dx,
            MySquare = request.MySquare,
            DxSquare = request.DxSquare,
            CommonStart = start,
            CommonEnd = end,
            Geostationary = geo,
            OrbitNumber = my.OrbitNumber,
            MyMiniPath = ComputeMiniPath(my, start, end, cancellation),
            DxMiniPath = ComputeMiniPath(dx, start, end, cancellation),
            MyMaxElevation = MaxElevationInRange(my, start, end, cancellation),
            DxMaxElevation = MaxElevationInRange(dx, start, end, cancellation)
          };
          overlaps.Add(overlap);
        }

        if (i % 4 == 0 || i == myPasses.Count - 1)
          progress.Report(new PredictionProgress(
            70 + 29 * (i + 1) / Math.Max(1, myPasses.Count),
            "QsoScheduler.ProgressOverlap"));
      }
      cancellation.ThrowIfCancellationRequested();
      progress.Report(new PredictionProgress(100, "QsoScheduler.ProgressOverlap"));
      return overlaps.OrderBy(o => o.CommonStart).ToList();
    }

    private static GroundStation MakeStation(string square, double altitudeMeters)
    {
      var pos = GridSquare.ToGeoPoint(square);
      var location = new GeodeticCoordinate(
        Angle.FromRadians(pos.LatitudeRad),
        Angle.FromRadians(pos.LongitudeRad),
        altitudeMeters / 1000d);
      return new GroundStation(location);
    }

    private const int MiniPathSteps = 10;
    private static PointF[] ComputeMiniPath(SatellitePass pass, DateTime start, DateTime end,
      CancellationToken cancellation)
    {
      var duration = end - start;
      if (duration <= TimeSpan.Zero) return Array.Empty<PointF>();
      var step = duration / MiniPathSteps;
      var path = new PointF[MiniPathSteps + 1];

      for (int i = 0; i <= MiniPathSteps; i++)
      {
        cancellation.ThrowIfCancellationRequested();
        var utc = start + step * i;
        var observation = pass.GetObservationAt(utc);
        if (observation == null) continue;
        double ro = 1 - observation.Elevation.Radians / Utils.HalfPi;
        double phi = Utils.HalfPi - observation.Azimuth.Radians;
        path[i] = new PointF((float)(ro * Math.Cos(phi)), (float)(ro * Math.Sin(phi)));
      }
      return path;
    }

    private static double MaxElevationInRange(SatellitePass pass, DateTime start, DateTime end,
      CancellationToken cancellation)
    {
      if (pass.Geostationary) return pass.MaxElevation;

      const int Steps = 30;
      var duration = end - start;
      if (duration <= TimeSpan.Zero) return 0;
      var step = duration / Steps;
      double max = 0;

      for (int i = 0; i <= Steps; i++)
      {
        cancellation.ThrowIfCancellationRequested();
        var utc = start + step * i;
        var observation = pass.GetObservationAt(utc);
        if (observation == null) continue;
        double el = observation.Elevation.Degrees;
        if (el > max) max = el;
      }
      return max;
    }



    //----------------------------------------------------------------------------------------------
    //                                        draw
    //----------------------------------------------------------------------------------------------
    private void PredictionList_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
      e.DrawBackground();

      var overlap = (OverlapPass)e.Item.Tag!;
      int h = e.Bounds.Height - 1;
      int w = e.Bounds.Width - 2 * h;
      bool now = overlap.CommonStart < DateTime.UtcNow;

      // sat name
      string text = overlap.Satellite.name;
      var size = e.Graphics.MeasureString(text, BoldFont);
      var rect = new RectangleF(e.Bounds.X, e.Bounds.Y, size.Width, size.Height);
      Brush brush = SystemBrushes.Window;
      if (overlap.Satellite.Flags.HasFlag(SatelliteFlags.Uhf)) brush = Theme.UhfTintBrush;
      else if (overlap.Satellite.Flags.HasFlag(SatelliteFlags.Vhf)) brush = Theme.VhfTintBrush;
      e.Graphics.FillRectangle(brush, rect);
      e.Graphics.DrawString(text, BoldFont, Theme.RowTextBrush(false), rect);

      // JPL tracking targets use an internal pass key rather than a
      // physical revolution number.
      text = overlap.Satellite.IsEphemerisTarget
        ? "JPL"
        : $"#{overlap.OrbitNumber}";
      size = e.Graphics.MeasureString(text, PredictionList.Font);
      rect = new RectangleF(rect.Right + 10, rect.Y, size.Width, size.Height);
      e.Graphics.DrawString(text, PredictionList.Font, SystemBrushes.WindowText, rect);

      // start/end time for the common segment
      text = overlap.Geostationary
        ? UiLocalizationService.Get("QsoScheduler.Geostationary")
        : UiLocalizationService.Format("QsoScheduler.TimeRange",
          ClockWidget.FromUtc(overlap.CommonStart),
          ClockWidget.FromUtc(overlap.CommonEnd), ClockWidget.Suffix);
      size = e.Graphics.MeasureString(text, PredictionList.Font);
      rect = new RectangleF(e.Bounds.X, e.Bounds.Y + e.Bounds.Height - size.Height - 2, size.Width, size.Height);
      e.Graphics.DrawString(text, PredictionList.Font, SystemBrushes.WindowText, rect);

      // duration of common segment + max elevation per station
      string elevText = UiLocalizationService.Format("QsoScheduler.Elevations",
        overlap.MyMaxElevation, overlap.DxMaxElevation);
      text = overlap.Geostationary ?
        elevText : $"{Utils.TimespanToString(overlap.CommonEnd - overlap.CommonStart, false)}   {elevText}";
      size = e.Graphics.MeasureString(text, PredictionList.Font);
      rect = new RectangleF(e.Bounds.X + w - size.Width, e.Bounds.Y + e.Bounds.Height - size.Height - 2, size.Width, size.Height);
      e.Graphics.DrawString(text, PredictionList.Font, SystemBrushes.WindowText, rect);

      // wait time to start of common segment
      text = now ? UiLocalizationService.Get("QsoScheduler.Now")
        : UiLocalizationService.Format("QsoScheduler.In",
          Utils.TimespanToString(overlap.CommonStart - DateTime.UtcNow));
      brush = now ? Theme.NowBrush : SystemBrushes.WindowText;
      size = e.Graphics.MeasureString(text, BoldFont);
      rect = new RectangleF(e.Bounds.X + w - size.Width, e.Bounds.Y, size.Width, size.Height);
      e.Graphics.DrawString(text, BoldFont, brush, rect);

      // two mini sky-views, side by side: me on left, dx on right
      DrawMiniSkyView(e.Graphics, new RectangleF(e.Bounds.X + w, e.Bounds.Y, h, h), overlap.MyMiniPath);
      DrawMiniSkyView(e.Graphics, new RectangleF(e.Bounds.X + w + h, e.Bounds.Y, h, h), overlap.DxMiniPath);

      // item separator
      var sep = new RectangleF(e.Bounds.X, e.Bounds.Y + h, e.Bounds.Width, 1);
      e.Graphics.FillRectangle(SystemBrushes.ControlDark, sep);
    }

    private void DrawMiniSkyView(Graphics g, RectangleF rect, PointF[] path)
    {
      rect.Inflate(-5, -5);
      var radius = rect.Width / 2;
      var center = new PointF(rect.Left + radius, rect.Top + radius);

      // grid: horizon, 45° ring, cross hairs
      g.DrawEllipse(Pens.Silver, rect); // fixed: mini sky view grid, silver in both themes
      g.DrawLine(Pens.Silver, rect.Left, center.Y, rect.Right, center.Y); // fixed: grid
      g.DrawLine(Pens.Silver, center.X, rect.Top, center.X, rect.Bottom); // fixed: grid
      var inner = rect;
      inner.Inflate(-radius / 2, -radius / 2);
      g.DrawEllipse(Pens.Silver, inner); // fixed: grid

      if (path != null && path.Length > 1)
      {
        var points = path.Select(p =>
          new PointF(center.X + p.X * radius, center.Y - p.Y * radius)).ToArray();
        g.DrawLines(PathPen, points);

        // green start, red end
        g.FillEllipse(Theme.NowBrush, points.First().X - 3, points.First().Y - 3, 6, 6);
        g.FillEllipse(Brushes.Red, points.Last().X - 3, points.Last().Y - 3, 6, 6);
      }
    }
  }
}
