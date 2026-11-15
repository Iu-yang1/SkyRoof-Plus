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
    Assert.True(QsoSchedulerPanel.TryCommonWindow(
      t.AddMinutes(-4), t.AddMinutes(11),
      t.AddMinutes(-2), t.AddMinutes(8),
      t, t.AddMinutes(7), out var start, out var end));
    Assert.Equal(t, start);
    Assert.Equal(t.AddMinutes(7), end);

    Assert.True(QsoSchedulerPanel.TryCommonWindow(
      t.AddSeconds(-100), t.AddMinutes(5),
      t.AddSeconds(-50), t.AddMinutes(5),
      t, t.AddSeconds(30), out _, out _));
    Assert.False(QsoSchedulerPanel.TryCommonWindow(
      t.AddSeconds(-100), t.AddMinutes(5),
      t.AddSeconds(-50), t.AddMinutes(5),
      t, t.AddSeconds(29), out _, out _));
    Assert.False(QsoSchedulerPanel.TryCommonWindow(
      t, t.AddMinutes(2),
      t.AddMinutes(3), t.AddMinutes(4),
      t, t.AddHours(1), out _, out _));
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
        using var panel = new QsoSchedulerPanel();
        using var scale = new UniformControlScale(panel, maxScale: 2f);
        scale.Apply(percent / 100f);
        panel.CreateControl();
        var progress = Field<ProgressBar>(panel, "CalculationProgress");
        var button = Field<Button>(panel, "CalculateButton");
        var start = Field<DateTimePicker>(panel, "StartUtcPicker");
        var end = Field<DateTimePicker>(panel, "EndUtcPicker");
        var status = Field<Label>(panel, "StatusLabel");

        foreach (var language in new[] { UiLanguage.English, UiLanguage.SimplifiedChinese })
        {
          UiLocalizationService.Apply(language);
          UiLocalizationService.ApplyStaticControls(panel);
          panel.RefreshLocalizedUi();
          Assert.Equal(UiLocalizationService.Get("QsoScheduler.Calculate"), button.Text);
          Assert.False(panel.IsPredictionRunning);
          Assert.True(start.Right < end.Left);
          Assert.True(progress.Right < status.Left);
          Assert.False(progress.Visible);

          string? output = Environment.GetEnvironmentVariable("SKYROOF_BILINGUAL_UI_ARTIFACT_DIR");
          if (!string.IsNullOrWhiteSpace(output))
          {
            Directory.CreateDirectory(output);
            using var bitmap = new Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output,
              $"pass-match-{(language == UiLanguage.English ? "en" : "zh")}-{percent}.png"));
          }
        }
      }
      finally { UiLocalizationService.Apply(previous); }
    });
  }
}
