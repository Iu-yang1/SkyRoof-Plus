using System.Drawing;
using VE3NEA;

namespace SkyRoof
{
  // Operator-oriented G-5500 panel. The GS-232 protocol and all motion/safety
  // state machines remain owned by RotatorWidget and its selected engine.
  internal sealed class YaesuG5500ControlCard : UserControl
  {
    private readonly Label connection = new();
    private readonly Label azimuth = new();
    private readonly Label elevation = new();
    private readonly Label target = new();
    private readonly Label satellite = new();
    private readonly Label hint = new();
    private readonly ProgressBar azimuthBar = new();
    private readonly ProgressBar elevationBar = new();
    private readonly RotatorDirectionWheel wheel = new();
    private readonly NumericUpDown targetAzimuth = new();
    private readonly NumericUpDown targetElevation = new();
    private readonly Button stopButton = new();
    private readonly Button trackButton = new();
    private readonly Button parkButton = new();
    private readonly Button goButton = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();

    private Context? context;
    private RotatorWidget? widget;
    private bool jogging;

    internal YaesuG5500ControlCard()
    {
      AutoScaleMode = AutoScaleMode.Font;
      BackColor = SystemColors.Control;
      ForeColor = SystemColors.ControlText;
      Padding = new Padding(10);
      Margin = Padding.Empty;
      Size = new Size(364, 455);
      MinimumSize = Size;
      MaximumSize = Size;

      BuildIndicators();
      BuildManualControls();
      BuildTargetControls();
      StartRefreshTimer();
    }

    private void BuildIndicators()
    {
      AddLabel("YAESU G-5500", new Rectangle(12, 10, 192, 25), true);
      connection.Location = new Point(210, 12);
      connection.Size = new Size(140, 22);
      connection.TextAlign = ContentAlignment.MiddleRight;
      Controls.Add(connection);

      AddLabel("GS-232A/B interface  ·  AZ 0–450° / EL 0–180°",
        new Rectangle(12, 37, 338, 20));

      azimuth.Location = new Point(12, 64);
      azimuth.Size = new Size(338, 21);
      azimuth.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
      Controls.Add(azimuth);
      azimuthBar.Location = new Point(12, 88);
      azimuthBar.Size = new Size(338, 12);
      azimuthBar.Minimum = 0;
      azimuthBar.Maximum = 450;
      Controls.Add(azimuthBar);

      elevation.Location = new Point(12, 109);
      elevation.Size = new Size(338, 21);
      elevation.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
      Controls.Add(elevation);
      elevationBar.Location = new Point(12, 133);
      elevationBar.Size = new Size(338, 12);
      elevationBar.Minimum = 0;
      elevationBar.Maximum = 180;
      Controls.Add(elevationBar);

    }

    private void BuildManualControls()
    {
      wheel.Location = new Point(12, 157);
      wheel.Size = new Size(202, 202);
      wheel.MoveStarted += direction =>
      {
        jogging = true;
        widget?.BeginManualContinuousMove(direction);
      };
      wheel.MoveStopped += (_, _) => CancelHeldMove();
      wheel.StopRequested += (_, _) =>
      {
        jogging = false;
        widget?.StopRotation();
      };
      Controls.Add(wheel);

      hint.Location = new Point(223, 160);
      hint.Size = new Size(128, 56);
      hint.Text = "Hold = MOVE\nRelease = STOP\nCenter = STOP";
      hint.ForeColor = SystemColors.GrayText;
      Controls.Add(hint);

      stopButton.Text = "STOP";
      stopButton.Location = new Point(223, 226);
      stopButton.Size = new Size(127, 31);
      stopButton.Click += (_, _) => widget?.StopRotation();
      Controls.Add(stopButton);

      trackButton.Text = "TRACK";
      trackButton.Location = new Point(223, 266);
      trackButton.Size = new Size(127, 29);
      trackButton.Click += (_, _) => widget?.ToggleTracking();
      Controls.Add(trackButton);

      parkButton.Text = "PARK";
      parkButton.Location = new Point(223, 305);
      parkButton.Size = new Size(127, 29);
      parkButton.MouseUp += ParkButton_MouseUp;
      Controls.Add(parkButton);

    }

    private void BuildTargetControls()
    {
      target.Location = new Point(12, 365);
      target.Size = new Size(338, 18);
      Controls.Add(target);
      satellite.Location = new Point(12, 385);
      satellite.Size = new Size(338, 18);
      Controls.Add(satellite);

      AddLabel("AZ", new Rectangle(12, 416, 26, 23));
      targetAzimuth.Location = new Point(38, 416);
      targetAzimuth.Size = new Size(73, 23);
      targetAzimuth.DecimalPlaces = 1;
      targetAzimuth.Maximum = 450;
      Controls.Add(targetAzimuth);

      AddLabel("EL", new Rectangle(120, 416, 26, 23));
      targetElevation.Location = new Point(146, 416);
      targetElevation.Size = new Size(73, 23);
      targetElevation.DecimalPlaces = 1;
      targetElevation.Maximum = 180;
      Controls.Add(targetElevation);

      goButton.Text = "GO";
      goButton.Location = new Point(228, 415);
      goButton.Size = new Size(122, 26);
      goButton.Click += (_, _) =>
        widget?.ManualMoveToDegrees((double)targetAzimuth.Value,
          (double)targetElevation.Value);
      Controls.Add(goButton);

    }

    private void StartRefreshTimer()
    {
      refreshTimer.Interval = 500;
      refreshTimer.Tick += (_, _) =>
      {
        if (Visible && Parent?.Visible == true)
          RefreshState();
      };
      refreshTimer.Start();
    }

    private void AddLabel(string text, Rectangle bounds, bool bold = false)
    {
      var label = new Label
      {
        Text = text,
        Location = bounds.Location,
        Size = bounds.Size,
        TextAlign = ContentAlignment.MiddleLeft
      };
      if (bold) label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      Controls.Add(label);
    }

    internal void Attach(Context ctx, RotatorWidget rotator)
    {
      context = ctx;
      widget = rotator;
      RefreshState();
    }

    internal void RefreshState()
    {
      if (context == null || widget == null) return;
      var settings = context.Settings.Rotator;
      bool connected = widget.IsRunning();
      bool enabled = settings.Enabled;
      bool locked = widget.IsManualControlLocked;
      bool movementAllowed = enabled && connected &&
        !locked && !widget.PositionSafetyTripped;

      connection.Text = !enabled ? "Disabled" :
        widget.PositionSafetyTripped ? "Position fault" :
        widget.ManagementReviewRequired ? "Safety lock" :
        !connected ? "Connecting" : locked ? "Live Track" : "Connected";
      connection.ForeColor = !enabled ? SystemColors.GrayText :
        connected && !widget.PositionSafetyTripped ? Color.Green : Color.IndianRed;

      var actual = widget.GetManualActualBearing();
      var commanded = widget.GetManualTargetBearing();
      var sat = widget.SatelliteBearing;
      azimuth.Text = "AZ   " + FormatAxis(actual?.AzDeg, 450);
      elevation.Text = "EL   " + FormatAxis(actual?.ElDeg, 180);
      SetGauge(azimuthBar, actual?.AzDeg);
      SetGauge(elevationBar, actual?.ElDeg);

      target.Text = "Target    " + FormatBearing(commanded);
      satellite.Text = "Satellite " + FormatBearing(sat);
      wheel.ActualBearing = actual;
      wheel.TargetBearing = commanded;
      wheel.SatelliteBearing = sat;
      wheel.Enabled = movementAllowed;
      wheel.Invalidate();

      SetRange(targetAzimuth, settings.MinAzimuth, settings.MaxAzimuth);
      SetRange(targetElevation, settings.MinElevation, settings.MaxElevation);
      if (!targetAzimuth.Focused)
        SetValue(targetAzimuth, commanded?.AzDeg ?? actual?.AzDeg);
      if (!targetElevation.Focused)
        SetValue(targetElevation, commanded?.ElDeg ?? actual?.ElDeg);
      targetAzimuth.Enabled = movementAllowed;
      targetElevation.Enabled = movementAllowed;
      goButton.Enabled = movementAllowed;
      stopButton.Enabled = enabled;
      parkButton.Enabled = enabled;
      parkButton.Text = widget.ParkProgressText;
      trackButton.Enabled = enabled && widget.TrackCheckbox.Enabled;
      trackButton.Text = widget.IsTracking ? "TRACK ✓" : "TRACK";
      hint.Text = locked ? "LIVE TRACK\nManual locked\nClear TRACK first" :
        widget.IsParking ? "PARK ACTIVE\nSTOP cancels" :
        "Hold = MOVE\nRelease = STOP\nCenter = STOP";
    }

    internal static string FormatAxis(double? degrees, int maximum) =>
      degrees is { } value && double.IsFinite(value)
        ? $"{value:F1}°   (0–{maximum}°)" : $"---   (0–{maximum}°)";

    private static string FormatBearing(Bearing? bearing) =>
      bearing == null ? "AZ ---   EL ---" :
        $"AZ {bearing.AzDeg:F1}°   EL {bearing.ElDeg:F1}°";

    private static void SetGauge(ProgressBar bar, double? value)
    {
      bar.Value = value is { } degrees && double.IsFinite(degrees)
        ? Math.Clamp((int)Math.Round(degrees), bar.Minimum, bar.Maximum)
        : bar.Minimum;
    }

    private static void SetRange(NumericUpDown spinner, int min, int max)
    {
      decimal lower = Math.Min(min, max);
      decimal upper = Math.Max(min, max);
      if (spinner.Minimum == lower && spinner.Maximum == upper) return;
      decimal current = Math.Clamp(spinner.Value, lower, upper);
      spinner.Minimum = lower;
      spinner.Maximum = upper;
      spinner.Value = current;
    }

    private static void SetValue(NumericUpDown spinner, double? degrees)
    {
      if (degrees is not { } value || !double.IsFinite(value)) return;
      spinner.Value = Math.Clamp((decimal)value, spinner.Minimum, spinner.Maximum);
    }

    internal void CancelHeldMove()
    {
      if (!jogging) return;
      jogging = false;
      widget?.EndManualContinuousMove();
    }

    private void ParkButton_MouseUp(object? sender, MouseEventArgs e)
    {
      if (context == null || widget == null) return;
      if (e.Button == MouseButtons.Left)
        widget.ManualPark();
      else if (e.Button == MouseButtons.Right)
      {
        using var dialog = new ParkPositionDialog(context,
          widget.GetManualActualBearing());
        dialog.ShowDialog(context.MainForm);
      }
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        CancelHeldMove();
        refreshTimer.Stop();
        refreshTimer.Dispose();
        wheel.Dispose();
      }
      base.Dispose(disposing);
    }
  }
}
