using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

[Collection("BilingualUiAcceptance")]
public sealed class QsoSchedulerManualSearchTests
{
  private static T Field<T>(object view, string field) where T : Control =>
    (T)(view.GetType().GetField(field,
      BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view)
      ?? throw new InvalidOperationException($"Missing QSO Scheduler control: {field}"));

  private static void Sta(Action test)
  {
    Exception? error = null;
    var thread = new Thread(() =>
    {
      try { test(); }
      catch (Exception ex) { error = ex; }
    });
    thread.IsBackground = true;
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Pass matching UI test timed out");
    if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
  }

  [Fact]
  public void InputEditsNeverLaunchPredictionsAndDefaultToFourteenUtcDays()
  {
    Sta(() =>
    {
      using var panel = new QsoSchedulerPanel();
      var grid = Field<TextBox>(panel, "DxSquareEdit");
      var start = Field<DateTimePicker>(panel, "StartUtcPicker");
      var end = Field<DateTimePicker>(panel, "EndUtcPicker");
      var button = Field<Button>(panel, "CalculateButton");
      var progress = Field<ProgressBar>(panel, "CalculationProgress");
      var list = Field<ListView>(panel, "PredictionList");
      var satellite = Field<ComboBox>(panel, "SatelliteComboBox");

      Assert.Equal(TimeSpan.FromDays(14), end.Value - start.Value);
      Assert.False(panel.IsPredictionRunning);
      Assert.False(progress.Visible);
      Assert.False(button.Enabled);

      satellite.Items.Add(new SatnogsDbSatellite
      {
        sat_id = "test-ao123", name = "AO-123"
      });
      satellite.SelectedIndex = 0;
      foreach (string part in new[] { "O", "OL", "OL7", "OL79", "OL79W", "OL79WQ" })
      {
        grid.Text = part;
        Assert.False(panel.IsPredictionRunning);
        Assert.Equal(0, list.VirtualListSize);
        Assert.False(progress.Visible);
      }
      start.Value = start.Value.AddDays(1);
      end.Value = end.Value.AddDays(2);
      Assert.Equal(TimeSpan.FromDays(15), end.Value - start.Value);
      Assert.False(panel.IsPredictionRunning);
      Assert.Equal(0, list.VirtualListSize);
      Assert.False(button.Enabled); // Designer-only panel has no station Context.

      end.Value = start.Value;
      Assert.False(QsoSchedulerPanel.HasValidRange(start.Value, end.Value));
      end.Value = start.Value.AddHours(1);
      Assert.True(QsoSchedulerPanel.HasValidRange(start.Value, end.Value));
    });
  }

  [Fact]
  public void CommonWindowIsClippedToRequestedUtcRangeAndRequiresThirtySeconds()
  {
    DateTime t = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    var result = QsoSchedulerPanel.IntersectVisibility(
      new(t.AddMinutes(-4), t.AddMinutes(11)),
      new(t.AddMinutes(-2), t.AddMinutes(8)),
      new(t, t.AddMinutes(7)));
    Assert.NotNull(result);
    Assert.Equal(t, result.Value.Start);
    Assert.Equal(t.AddMinutes(7), result.Value.End);

    Assert.NotNull(QsoSchedulerPanel.IntersectVisibility(
      new(t.AddSeconds(-100), t.AddMinutes(5)),
      new(t.AddSeconds(-50), t.AddMinutes(5)),
      new(t, t.AddSeconds(30))));
    Assert.Null(QsoSchedulerPanel.IntersectVisibility(
      new(t.AddSeconds(-100), t.AddMinutes(5)),
      new(t.AddSeconds(-50), t.AddMinutes(5)),
      new(t, t.AddSeconds(29))));
    Assert.Null(QsoSchedulerPanel.IntersectVisibility(
      new(t, t.AddMinutes(2)),
      new(t.AddMinutes(3), t.AddMinutes(4)),
      new(t, t.AddHours(1))));
  }

  [Theory]
  [InlineData(100)]
  [InlineData(125)]
  [InlineData(150)]
  [InlineData(200)]
  public void BothLanguagesRenderRangeControlsWithoutStartingPrediction(int percent)
  {
    Sta(() =>
    {
      UiLanguage previous = SettingsLanguage.Current;
      try
      {
        // A DockContent Form can return a completely blank DrawToBitmap on
        // headless Windows runners. Render its ACTUAL toolbar controls on a
        // plain canvas and reject blank captures instead.
        foreach (var language in new[] { UiLanguage.English, UiLanguage.SimplifiedChinese })
        {
          UiLocalizationService.Apply(language);
          using var panel = new QsoSchedulerPanel();
          UiLocalizationService.ApplyStaticControls(panel);
          panel.RefreshLocalizedUi();

          float ratio = percent / 100f;
          var progress = Field<ProgressBar>(panel, "CalculationProgress");
          var button = Field<Button>(panel, "CalculateButton");
          var start = Field<DateTimePicker>(panel, "StartUtcPicker");
          var end = Field<DateTimePicker>(panel, "EndUtcPicker");
          var status = Field<Label>(panel, "StatusLabel");

          // Scale a non-Form canvas so 200% is not capped by the CI
          // desktop work area (the native DockContent Form is screen-bound).
          using var host = new Panel
          {
            Size = new Size(600, 100),
            BackColor = panel.BackColor
          };
          foreach (Control control in panel.Controls.Cast<Control>().ToArray())
          {
            if (control.Name is "dockPanel1" or "PredictionList") continue;
            panel.Controls.Remove(control);
            host.Controls.Add(control);
          }
          using var scale = new UniformControlScale(host, maxScale: 2f);
          scale.Apply(ratio);
          host.CreateControl();
          foreach (Control child in host.Controls) child.CreateControl();

          Assert.Equal(new Size((int)(600 * ratio), (int)(100 * ratio)), host.Size);
          Assert.Equal(UiLocalizationService.Get("QsoScheduler.Calculate"), button.Text);
          Assert.False(panel.IsPredictionRunning);
          Assert.True(start.Right < end.Left);
          Assert.True(progress.Right < status.Left);
          Assert.True(end.Right <= host.ClientSize.Width, "UTC end picker is clipped");
          Assert.True(button.Right <= host.ClientSize.Width, "Start button is clipped");
          Assert.True(status.Right <= host.ClientSize.Width, "Progress text is clipped");
          Assert.False(progress.Visible);

          string? output = Environment.GetEnvironmentVariable("SKYROOF_BILINGUAL_UI_ARTIFACT_DIR");
          if (!string.IsNullOrWhiteSpace(output))
          {
            Directory.CreateDirectory(output);
            using var bitmap = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            string file = Path.Combine(output,
              $"pass-match-{(language == UiLanguage.English ? "en" : "zh")}-{percent}.png");
            bitmap.Save(file);

            Color background = bitmap.GetPixel(bitmap.Width - 2, bitmap.Height - 2);
            int painted = 0;
            for (int y = 4; y < bitmap.Height - 4; y += 4)
              for (int x = 4; x < bitmap.Width - 4; x += 4)
                if (bitmap.GetPixel(x, y).ToArgb() != background.ToArgb())
                  painted++;
            Assert.True(painted > 100,
              $"Blank pass-matching toolbar capture: {file} ({painted} pixels)");
          }
        }
      }
      finally { UiLocalizationService.Apply(previous); }
    });
  }

}
