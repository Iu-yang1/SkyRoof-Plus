using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class UiDpiVisualAcceptanceTests
{
  [Theory]
  [InlineData("SkyView", 360, 96)]
  [InlineData("SkyView", 360, 144)]
  [InlineData("SkyView", 720, 192)]
  [InlineData("SatellitePasses", 360, 96)]
  [InlineData("SatellitePasses", 360, 150)]
  [InlineData("SatellitePasses", 720, 192)]
  [InlineData("IcomSpectrum", 720, 96)]
  [InlineData("IcomSpectrum", 720, 144)]
  [InlineData("IcomSpectrum", 1080, 192)]
  [InlineData("CwSkimmer", 720, 96)]
  [InlineData("CwSkimmer", 720, 144)]
  [InlineData("CwSkimmer", 1080, 192)]
  public void DockSurfaceDoesNotReflowOrLoseControlActions(
    string name, int width, int dpiPercent)
  {
    RunSta(() =>
    {
      foreach (bool dark in new[] { false, true })
        VerifyOneDockFixture(name, width, dpiPercent, dark);
    });
  }

  private static void VerifyOneDockFixture(
    string name, int width, int dpiPercent, bool dark)
  {
    using var root = BuildDockFixture(name, width, dark);
    var buttons = root.Controls.Cast<Control>()
      .SelectMany(c => c.Controls.Cast<Control>()).OfType<Button>().ToArray();
    var originalOrders = buttons.Select(c => c.Name).ToArray();
    int actions = 0;
    foreach (var button in buttons) button.Click += (_, _) => ++actions;
    using var scaler = new UniformControlScale(root, scaleRootBounds: false);
    var original = root.Bounds;
    float factor = Math.Min(dpiPercent / 100f,
      Math.Max(.5f, (width - 12) / 360f));
    scaler.Apply(factor);
    Assert.Equal(original, root.Bounds);
    Assert.Equal(originalOrders, buttons.Select(c => c.Name).ToArray());
    Assert.All(buttons, button =>
    {
      Assert.True(button.Visible);
      Assert.True(button.Parent is Control host &&
        button.Right <= host.ClientSize.Width &&
        button.Bottom <= host.ClientSize.Height);
      button.PerformClick();
    });
    Assert.Equal(buttons.Length, actions);
    Assert.False(root.AutoScroll);
    var caption = root.Controls.OfType<Label>().Single();
    int rendered = TextRenderer.MeasureText(caption.Text, caption.Font,
      Size.Empty, TextFormatFlags.SingleLine).Width;
    Assert.True(rendered <= caption.ClientSize.Width,
      $"Caption clipped in {name}, {width}px, simulated {dpiPercent}%");
    Assert.All(root.Controls.OfType<FlowLayoutPanel>(), p =>
    {
      Assert.False(p.WrapContents);
      Assert.False(p.AutoScroll);
    });

    using var bitmap = new Bitmap(root.Width, root.Height);
    root.CreateControl();
    root.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    string? dir = Environment.GetEnvironmentVariable("SKYROOF_UI_DPI_ARTIFACT_DIR");
    if (string.IsNullOrWhiteSpace(dir)) return;
    Directory.CreateDirectory(dir);
    bitmap.Save(Path.Combine(dir,
      $"dock-{name}-{width}-{dpiPercent}-{(dark ? "dark" : "light")}.png"));
  }

  private static Panel BuildDockFixture(string name, int width, bool dark)
  {
    Color bg = dark ? Color.FromArgb(32, 34, 38) : Color.White;
    Color fg = dark ? Color.WhiteSmoke : Color.Black;
    var root = new Panel
    {
      Name = name, Size = new Size(width, 360), BackColor = bg,
      ForeColor = fg, AutoScroll = false
    };
    var toolbar = new FlowLayoutPanel
    {
      Name = "Controls", Dock = DockStyle.Top, Height = 44,
      FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
      AutoScroll = false, BackColor = bg, Padding = new Padding(5)
    };
    foreach (string title in new[] { "TRACK", "PARK", "STOP" })
      toolbar.Controls.Add(new Button
      {
        Name = title, Text = title, Width = 100, Height = 30,
        Font = new Font("Segoe UI", 10f), Margin = new Padding(2)
      });

    var caption = new Label
    {
      Text = name switch
      {
        "SkyView" => "Sky View — AZ / EL",
        "SatellitePasses" => "Satellite Passes — AOS / LOS",
        "IcomSpectrum" => "Icom LAN Spectrum — REF / VBW / AVG / ZOOM",
        _ => "CW Skimmer — Pileup / RX / TX"
      },
      Dock = DockStyle.Top, Height = 36, AutoSize = false,
      ForeColor = fg, TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font("Segoe UI", 11f, FontStyle.Bold)
    };
    var spectrum = new Panel
    {
      Name = "Visualization", Dock = DockStyle.Fill, BackColor = bg
    };
    spectrum.Paint += (_, e) =>
    {
      var rect = spectrum.ClientRectangle;
      using var pen = new Pen(dark ? Color.SteelBlue : Color.Gray, 2f);
      var center = new Point(rect.Width / 2, rect.Height / 2);
      int radius = Math.Max(10, Math.Min(rect.Width, rect.Height) / 3);
      e.Graphics.DrawEllipse(pen, center.X - radius, center.Y - radius,
        radius * 2, radius * 2);
      e.Graphics.DrawLine(pen, center.X, 0, center.X, rect.Height);
      e.Graphics.DrawLine(pen, 0, center.Y, rect.Width, center.Y);
    };
    root.Controls.Add(spectrum);
    root.Controls.Add(caption);
    root.Controls.Add(toolbar);
    return root;
  }

  [Fact]
  public void SequentialZoomChoicesPreserveStopActionAndLayoutOrder()
  {
    RunSta(() =>
    {
      using var root = BuildDockFixture("SatellitePasses", 720, false);
      using var scaler = new UniformControlScale(root, false);
      var toolbar = root.Controls.OfType<FlowLayoutPanel>().Single();
      var stop = toolbar.Controls.OfType<Button>().Single(b => b.Name == "STOP");
      int stops = 0;
      stop.Click += (_, _) => stops++;
      var original = toolbar.Controls.Cast<Control>().Select(c => c.Name).ToArray();
      foreach (float factor in new[] { .75f, 1.25f, 1f, .85f, 1f })
      {
        scaler.Apply(factor);
        stop.PerformClick();
        Assert.Equal(original, toolbar.Controls.Cast<Control>().Select(c => c.Name));
        Assert.False(toolbar.WrapContents);
      }
      Assert.Equal(5, stops);
      Assert.Equal(100, stop.Width);
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
