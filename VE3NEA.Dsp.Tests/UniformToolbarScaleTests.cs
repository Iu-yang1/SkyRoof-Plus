using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class UniformToolbarScaleTests
{
  [Theory]
  [InlineData(1920, 1f, true)]
  [InlineData(1280, 1f, false)]
  [InlineData(1920, 1.25f, true)]
  [InlineData(800, 1f, false)]
  public void FitUsesOneFactorWithoutMovingOrReflowingControls(
    int windowWidth, float preference, bool photoExpected)
  {
    var result = UniformControlScale.FitToolbar(
      windowWidth, 1790, 1696, preference);
    Assert.Equal(photoExpected, result.ShowPhoto);
    int occupied = (int)Math.Ceiling(
      (result.ShowPhoto ? 1790 : 1696) * result.Factor);
    Assert.True(occupied <= windowWidth - 10);
  }

  [Fact]
  public void ToolbarBoundsAndFontsScaleFromBaselineWithoutAccumulatingRounding()
  {
    RunSta(() =>
    {
      using var panel = new Panel { Size = new Size(1600, 78) };
      using var sat = new UserControl { Name = "Satellite", Width = 340, Dock = DockStyle.Left };
      using var frequency = new UserControl { Name = "Frequency", Width = 682, Dock = DockStyle.Left };
      using var label = new Label
      {
        Name = "FrequencyValue", Text = "145.800.000", Location = new Point(20, 20),
        Size = new Size(170, 34), AutoSize = false,
        Font = new Font("Segoe UI", 15f)
      };
      frequency.Controls.Add(label);
      panel.Controls.Add(frequency);
      panel.Controls.Add(sat);
      using var scaler = new UniformControlScale(panel);
      Rectangle original = label.Bounds;
      float originalFont = label.Font.SizeInPoints;
      scaler.Apply(.8f);
      Rectangle once = label.Bounds;
      Assert.Equal(UniformControlScale.ScaleBounds(original, .8f), once);
      Assert.InRange(label.Font.SizeInPoints, originalFont * .8f - .1f,
        originalFont * .8f + .1f);
      scaler.Apply(1.2f);
      scaler.Apply(.8f);
      Assert.Equal(once, label.Bounds);
      Assert.Equal(UniformControlScale.Pixel(682, .8f), frequency.Width);
      Assert.Equal(UniformControlScale.Pixel(340, .8f), sat.Width);
      scaler.Apply(1f);
      Assert.Equal(original, label.Bounds);
    });
  }

  [Theory]
  [InlineData(1280, 96)]
  [InlineData(1280, 120)]
  [InlineData(1280, 144)]
  [InlineData(1280, 192)]
  [InlineData(1920, 96)]
  [InlineData(1920, 120)]
  [InlineData(1920, 144)]
  [InlineData(1920, 192)]
  public void RenderFixedToolbarDpiFixtures(int width, int dpi)
  {
    RunSta(() =>
    {
      using var root = new Panel
      {
        Size = new Size(width, 160), BackColor = Color.FromArgb(245, 246, 248)
      };
      using var toolbar = new Panel
      {
        Dock = DockStyle.Top, Height = 78, BackColor = Color.White
      };
      root.Controls.Add(toolbar);
      var names = new[]
      {
        ("Satellite / TX", 340), ("Photo", 90),
        ("RX/TX 145.800 MHz", 682), ("RF / AF", 210),
        ("AZ / EL / STOP", 210), ("PTT", 112)
      };
      foreach (var (name, w) in names)
      {
        var control = new UserControl
        {
          Dock = DockStyle.Left, Width = w, BorderStyle = BorderStyle.FixedSingle,
          BackColor = Color.FromArgb(236, 241, 247)
        };
        control.Controls.Add(new Label
        {
          Text = name, Location = new Point(6, 16),
          Width = Math.Max(20, w - 12), Height = 32,
          TextAlign = ContentAlignment.MiddleLeft,
          Font = new Font("Segoe UI", 10f)
        });
        toolbar.Controls.Add(control);
      }
      // Design fixture deliberately keeps controls in the same order.
      using var scaler = new UniformControlScale(toolbar);
      float physical = dpi / 96f;
      // System DPI has already scaled controls at startup; zoom simulates
      // a single proportional factor for the final canvas, not a reflow.
      var (factor, _) = UniformControlScale.FitToolbar(
        width, scaler.RequiredWidth() + 117, scaler.RequiredWidth() + 27, physical);
      scaler.Apply(factor);
      Assert.True(toolbar.Controls.Count == names.Length);
      foreach (Control control in toolbar.Controls)
        Assert.True(control.Width > 0 && control.Height > 0);
      using var bitmap = new Bitmap(root.Width, root.Height);
      root.CreateControl();
      root.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
      string? directory = Environment.GetEnvironmentVariable("SKYROOF_UI_DPI_ARTIFACT_DIR");
      if (!string.IsNullOrWhiteSpace(directory))
      {
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, $"toolbar-{width}-{dpi}.png"));
      }
    });
  }

  private static void RunSta(Action action)
  {
    Exception? error = null;
    var thread = new Thread(() =>
    {
      try { action(); }
      catch (Exception ex) { error = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
    if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
  }
}
