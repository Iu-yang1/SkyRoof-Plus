using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SkyRoof;
using VE3NEA;
using Xunit;

namespace VE3NEA.Dsp.Tests;

[Collection("BilingualUiAcceptance")]
public sealed class TopToolbarLocalizationLayoutTests
{
  private static T Field<T>(object target, string name) where T : Control =>
    (T)(target.GetType().GetField(name,
      BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
      ?? throw new InvalidOperationException($"Missing top toolbar control: {name}"));

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
    Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Toolbar layout test timed out");
    if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
  }

  [Theory]
  [InlineData(100)]
  [InlineData(125)]
  [InlineData(150)]
  [InlineData(200)]
  public void SatelliteAndFrequencyHeadersAlignInBothLanguages(int percent)
  {
    Sta(() =>
    {
      UiLanguage previous = SettingsLanguage.Current;
      try
      {
        using var selector = new SatelliteSelectorWidget();
        using var frequency = new FrequencyWidget();
        using var selectorScale = new UniformControlScale(selector, maxScale: 2f);
        using var frequencyScale = new UniformControlScale(frequency, maxScale: 2f);
        float ratio = percent / 100f;
        selectorScale.Apply(ratio);
        frequencyScale.Apply(ratio);
        selector.CreateControl();
        frequency.CreateControl();

        var satellite = Field<Label>(selector, "label2");
        var payload = Field<Label>(selector, "label3");
        var satelliteBox = Field<ComboBox>(selector, "SatelliteComboBox");
        var payloadBox = Field<ComboBox>(selector, "TransmitterComboBox");
        var downArrow = Field<Label>(frequency, "label2");
        var upArrow = Field<Label>(frequency, "label1");
        var downText = Field<Label>(frequency, "DownlinkLabel");
        var upText = Field<Label>(frequency, "UplinkLabel");
        var downMode = Field<ComboBox>(frequency, "DownlinkModeCombobox");
        var upMode = Field<ComboBox>(frequency, "UplinkModeCombobox");

        foreach (UiLanguage language in new[]
        {
          UiLanguage.SimplifiedChinese, UiLanguage.English
        })
        {
          UiLocalizationService.Apply(language);
          UiLocalizationService.ApplyStaticControls(selector);
          UiLocalizationService.ApplyStaticControls(frequency);
          frequency.RefreshLocalizedUi();
          selector.PerformLayout();
          frequency.PerformLayout();

          Assert.Equal(satellite.Right, payload.Right);
          Assert.True(satellite.Right < satelliteBox.Left);
          Assert.True(payload.Right < payloadBox.Left);
          Assert.Equal(satelliteBox.Left, payloadBox.Left);
          Assert.Equal(downArrow.Top, upArrow.Top);
          Assert.Equal(downText.Top, upText.Top);
          Assert.InRange(Math.Abs((downText.Left - downArrow.Right) -
            (upText.Left - upArrow.Right)), 0, 2);
          Assert.True(downArrow.Right < downText.Left);
          Assert.True(upArrow.Right < upText.Left);
          Assert.True(downText.Right < downMode.Left);
          Assert.True(upText.Right < upMode.Left);

          foreach (Label caption in new[] { satellite, payload, downText, upText })
            Assert.True(TextRenderer.MeasureText(caption.Text, caption.Font,
              Size.Empty, TextFormatFlags.NoPadding).Width <= caption.ClientSize.Width + 2,
              $"Clipped {language} {percent}% caption: {caption.Text}");

          string? directory = Environment.GetEnvironmentVariable(
            "SKYROOF_BILINGUAL_UI_ARTIFACT_DIR");
          if (!string.IsNullOrWhiteSpace(directory))
          {
            Directory.CreateDirectory(directory);
            string locale = language == UiLanguage.English ? "en" : "zh";
            using var satelliteCapture = new Bitmap(selector.Width, selector.Height);
            selector.DrawToBitmap(satelliteCapture, new Rectangle(Point.Empty, satelliteCapture.Size));
            satelliteCapture.Save(Path.Combine(directory,
              $"satellite-selector-{locale}-{percent}.png"));
            using var frequencyCapture = new Bitmap(frequency.Width, frequency.Height);
            frequency.DrawToBitmap(frequencyCapture, new Rectangle(Point.Empty, frequencyCapture.Size));
            frequencyCapture.Save(Path.Combine(directory,
              $"frequency-toolbar-{locale}-{percent}.png"));
          }
        }
      }
      finally { UiLocalizationService.Apply(previous); }
    });
  }

  [Fact]
  public void OwnerDrawTooltipsReserveRoomForAllMultilineTextEdges()
  {
    var method = typeof(ToolTipEx).GetMethod("AddTextInsets",
      BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(method);
    var expanded = (Size)method!.Invoke(null, [new Size(180, 68)])!;
    Assert.Equal(new Size(192, 78), expanded);
  }
}
