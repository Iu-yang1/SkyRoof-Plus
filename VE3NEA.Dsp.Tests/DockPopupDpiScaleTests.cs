using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class DockPopupDpiScaleTests
{
  [Theory]
  [InlineData(.75f)]
  [InlineData(.85f)]
  [InlineData(1.10f)]
  [InlineData(1.25f)]
  public void DockCanvasScalesOnlyItsChildrenNotDockPanelOwnedBounds(float factor)
  {
    RunSta(() =>
    {
      using var dock = new Panel { Bounds = new Rectangle(20, 20, 680, 480) };
      var fixedTop = new Panel { Dock = DockStyle.Top, Height = 48 };
      var content = new Label
      {
        Text = "Satellite Passes", Font = new Font("Segoe UI", 12f),
        AutoSize = false, Location = new Point(12, 8), Size = new Size(300, 32)
      };
      fixedTop.Controls.Add(content);
      dock.Controls.Add(fixedTop);
      Rectangle original = dock.Bounds;
      using var scale = new UniformControlScale(dock, scaleRootBounds: false);
      scale.Apply(factor);
      Assert.Equal(original, dock.Bounds);
      Assert.Equal(UniformControlScale.Pixel(48, factor), fixedTop.Height);
      Assert.Equal(UniformControlScale.ScaleBounds(
        new Rectangle(12, 8, 300, 32), factor), content.Bounds);
      scale.Apply(1f);
      Assert.Equal(original, dock.Bounds);
      Assert.Equal(48, fixedTop.Height);
    });
  }

  [Theory]
  [InlineData(75)]
  [InlineData(100)]
  [InlineData(125)]
  public void G5500PopupRetainsAllManualControlsAndRendersAtScale(int percent)
  {
    RunSta(() =>
    {
      using var card = new YaesuG5500ControlCard();
      Size baseline = card.Size;
      using var scaler = new UniformControlScale(card);
      float ratio = percent / 100f;
      scaler.Apply(ratio);
      Assert.Equal(UniformControlScale.Pixel(baseline.Width, ratio), card.Width);
      Assert.Equal(UniformControlScale.Pixel(baseline.Height, ratio), card.Height);
      foreach (Control control in card.Controls)
      {
        Assert.InRange(control.Left, 0, card.ClientSize.Width);
        Assert.True(control.Right <= card.ClientSize.Width + 2,
          $"{control.Name} clipped at {percent}%");
        Assert.True(control.Bottom <= card.ClientSize.Height + 2,
          $"{control.Name} vertically clipped at {percent}%");
      }
      using var bitmap = new Bitmap(card.Width, card.Height);
      card.CreateControl();
      card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
      string? dir = Environment.GetEnvironmentVariable("SKYROOF_UI_DPI_ARTIFACT_DIR");
      if (!string.IsNullOrWhiteSpace(dir))
      {
        Directory.CreateDirectory(dir);
        bitmap.Save(Path.Combine(dir, $"g5500-popup-{percent}.png"));
      }
    });
  }

  private static void RunSta(Action action)
  {
    Exception? failure = null;
    var worker = new Thread(() =>
    {
      try { action(); }
      catch (Exception ex) { failure = ex; }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    Assert.True(worker.Join(TimeSpan.FromSeconds(20)));
    if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
  }
}
