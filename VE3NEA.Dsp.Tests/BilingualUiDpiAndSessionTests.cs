using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using MathNet.Numerics;
using SkyRoof;
using SkyRoof.SSTV;
using VE3NEA.SkyTlm.Core;
using Xunit;

namespace VE3NEA.Dsp.Tests;

/// <summary>
/// Render the actual SSTV/Recorder/Rotator WinForms controls on the Windows
/// test runner. Scale is emulated in-process; physical monitor DPI is not
/// changed. This suite never accesses a real radio or audio endpoint.
/// </summary>
[CollectionDefinition("BilingualUiAcceptance", DisableParallelization = true)]
public sealed class BilingualUiAcceptanceCollection { }

[Collection("BilingualUiAcceptance")]
public sealed class BilingualUiDpiAndSessionTests
{
  private static T Field<T>(object target, string name) =>
    (T)(target.GetType().GetField(name,
      BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
      ?? throw new InvalidOperationException($"Missing UI field: {name}"));

  private static Context StubContext()
  {
    var context = (Context)RuntimeHelpers.GetUninitializedObject(typeof(Context));
    context.Settings = new Settings();
    return context;
  }

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
    Assert.True(thread.Join(TimeSpan.FromSeconds(85)),
      "Bilingual UI rendering or session check timed out on STA");
    if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
  }

  [Theory]
  [InlineData(100)]
  [InlineData(125)]
  [InlineData(150)]
  [InlineData(200)]
  public void ActualSstvViewsRenderBothLanguagesAndRetainOperatorState(int percent)
  {
    Sta(() =>
    {
      UiLanguage before = SettingsLanguage.Current;
      try
      {
        var ctx = StubContext();
        // The production panel constructs the real WinForms controls and
        // its receive/transmit sessions; no device starts in this fixture.
        using var panel = new SstvConsolePanel(ctx);
        panel.Show();
        Application.DoEvents();
        using var scaler = new UniformControlScale(panel, scaleRootBounds: false);
        UiLocalizationService.Bind(panel, panel.RefreshLocalizedUi);
        var tabs = panel.Controls.OfType<TabControl>().Single();
        var myCall = Field<TextBox>(panel, "myCallInput");
        var theirCall = Field<TextBox>(panel, "theirCallInput");
        var overlay = Field<TextBox>(panel, "overlayText");
        var templates = Field<ComboBox>(panel, "templateBox");
        var source = Field<ComboBox>(panel, "rxSourceBox");
        var receiver = Field<object>(panel, "receiveSession");
        var transmitter = Field<SstvTransmitSession>(panel, "transmitSession");
        int sourceIndex = source.SelectedIndex;
        int templateIndex = templates.SelectedIndex;
        myCall.Text = "BG5JSU";
        theirCall.Text = "HB9Q";
        overlay.Text = "QSO {HISCALL}";
        var baseSize = new Size(980, 720);
        float ratio = percent / 100f;
        panel.Size = new Size((int)(baseSize.Width * ratio),
          (int)(baseSize.Height * ratio));
        scaler.Apply(ratio);
        foreach (UiLanguage language in new[]
        {
          UiLanguage.SimplifiedChinese, UiLanguage.English
        })
        {
          UiLocalizationService.Apply(language);
          Application.DoEvents();
          string expected = language == UiLanguage.SimplifiedChinese
            ? "接收 RX" : "Receive RX";
          Assert.Equal(expected, tabs.TabPages[0].Text);
          Assert.Equal(language == UiLanguage.SimplifiedChinese
            ? "发送 TX" : "Transmit TX", tabs.TabPages[1].Text);

          // Physical decoder/transmitter objects and operator choices must
          // survive changes in text, even after control-scale layout.
          Assert.Same(receiver, Field<object>(panel, "receiveSession"));
          Assert.Same(transmitter, Field<SstvTransmitSession>(panel, "transmitSession"));
          Assert.False(transmitter.IsSending);
          Assert.Equal("BG5JSU", myCall.Text);
          Assert.Equal("HB9Q", theirCall.Text);
          Assert.Equal("QSO {HISCALL}", overlay.Text);
          Assert.Equal(templateIndex, templates.SelectedIndex);
          Assert.Equal(sourceIndex, source.SelectedIndex);

          for (int tab = 0; tab < 2; tab++)
          {
            tabs.SelectedIndex = tab;
            panel.PerformLayout();
            tabs.PerformLayout();
            Application.DoEvents();
            Assert.False(panel.AutoScroll);
            using var image = new Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            // Real content must be present: not just a blank DockContent
            // surface, unlike earlier false-positive screenshot runs.
            int nonBackground = 0;
            Color basePixel = image.GetPixel(image.Width / 2, 4);
            for (int y = 50; y < image.Height; y += 11)
              for (int x = 15; x < image.Width - 15; x += 11)
                if (image.GetPixel(x, y).ToArgb() != basePixel.ToArgb())
                  nonBackground++;
            Assert.True(nonBackground > 120,
              $"Blank SSTV {language} {percent}% tab {tab} capture");
            string? directory = Environment.GetEnvironmentVariable(
              "SKYROOF_BILINGUAL_UI_ARTIFACT_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
              Directory.CreateDirectory(directory);
              image.Save(Path.Combine(directory,
                $"sstv-{(language == UiLanguage.English ? "en" : "zh")}-{percent}-" +
                $"{(tab == 0 ? "rx" : "tx")}.png"));
            }
          }
        }
      }
      finally { UiLocalizationService.Apply(before); }
    });
  }

  [Fact]
  public void RealRecorderAndRotatorControlsPreserveSamplesAndManualInputs()
  {
    Sta(() =>
    {
      UiLanguage before = SettingsLanguage.Current;
      try
      {
        var ctx = StubContext();
        using var recorder = new RecorderPanel();
        using var manager = new RecordingManager(ctx);
        typeof(RecorderPanel).GetField("recordingManager",
          BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(recorder, manager);
        var iq = new[]
        {
          new Complex32(.25f, -.2f), new Complex32(-.5f, .4f)
        };
        typeof(RecordingManager).GetField("iqBuffer",
          BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, iq);
        typeof(RecordingManager).GetField("samplesInBuffer",
          BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, iq.Length);
        UiLocalizationService.Bind(recorder, recorder.RefreshLocalizedUi);
        using var rotator = new RotatorControlCard();
        var az = Field<NumericUpDown>(rotator, "AzimuthSpinner");
        var el = Field<NumericUpDown>(rotator, "ElevationSpinner");
        var step = Field<NumericUpDown>(rotator, "StepSpinner");
        az.Value = 120m;
        el.Value = 45m;
        step.Value = 2.5m;
        var beforeIq = manager.IqBuffer;
        int samples = manager.SamplesInBuffer;
        int position = manager.PlaybackPosition;
        foreach (UiLanguage language in new[]
        {
          UiLanguage.English, UiLanguage.SimplifiedChinese,
          UiLanguage.English
        })
        {
          UiLocalizationService.Apply(language);
          Assert.Same(beforeIq, manager.IqBuffer);
          Assert.Equal(samples, manager.SamplesInBuffer);
          Assert.Equal(position, manager.PlaybackPosition);
          Assert.False(manager.IsPlayingBack);
          Assert.False(manager.IsRecording);
          Assert.Equal(120m, az.Value);
          Assert.Equal(45m, el.Value);
          Assert.Equal(2.5m, step.Value);
          Assert.Equal(language == UiLanguage.English
            ? "Manual Rotator" : "手动旋转器",
            rotator.Controls.OfType<Label>().First().Text);
        }
      }
      finally { UiLocalizationService.Apply(before); }
    });
  }

  [Fact]
  public void LiveAo123KissDecoderKeepsPartialFrameAcrossLocaleChanges()
  {
    // The actual TM-short KISS transport holds per-VC packet state between
    // transfer frames. A localization event must not clear its pending data.
    UiLanguage before = SettingsLanguage.Current;
    try
    {
      var decoder = new AsrtuTmShortKissTransport();
      static Frame Transfer(params byte[] data)
      {
        byte[] frame = new byte[5 + data.Length];
        Array.Copy(data, 0, frame, 5, data.Length);
        return new Frame
        {
          Bytes = frame, Framing = Framing.CCSDS, CrcValid = true
        };
      }

      Assert.Empty(decoder.Push(Transfer(0xC0, 0x00, 0x31)).ToArray());
      UiLocalizationService.Apply(UiLanguage.English);
      Assert.Empty(decoder.Push(Transfer(0x32)).ToArray());
      UiLocalizationService.Apply(UiLanguage.SimplifiedChinese);
      var frames = decoder.Push(Transfer(0x33, 0xC0)).ToArray();
      Assert.Single(frames);
      Assert.Equal(new byte[] { 0x00, 0x31, 0x32, 0x33 }, frames[0].Bytes);
      Assert.Null(frames[0].CrcValid);
    }
    finally { UiLocalizationService.Apply(before); }
  }

  [Fact]
  public void DecoderSampleStreamMatchesUninterruptedReferenceAcrossLocaleChanges()
  {
    // Deterministic IF IQ conversion uses the same real production DSP, but
    // no connected telemetry device; this checks phase/history preservation.
    UiLanguage before = SettingsLanguage.Current;
    try
    {
      const int sampleRate = 48000;
      var subject = new TelemetryRealAudioIqConverter(12000);
      var reference = new TelemetryRealAudioIqConverter(12000);
      for (int segment = 0; segment < 8; segment++)
      {
        float[] pcm = Enumerable.Range(0, 503).Select(i =>
          (float)Math.Sin(2 * Math.PI * 13210 *
            (segment * 503 + i) / sampleRate)).ToArray();
        Complex32[] expected = reference.Convert(pcm);
        UiLocalizationService.Apply(segment % 2 == 0
          ? UiLanguage.SimplifiedChinese : UiLanguage.English);
        Complex32[] actual = subject.Convert(pcm);
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
          Assert.Equal(expected[i].Real, actual[i].Real);
          Assert.Equal(expected[i].Imaginary, actual[i].Imaginary);
        }
      }
    }
    finally { UiLocalizationService.Apply(before); }
  }
}
