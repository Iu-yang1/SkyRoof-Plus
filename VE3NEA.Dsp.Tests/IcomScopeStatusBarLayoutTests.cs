using System.Drawing;
using System.Windows.Forms;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class IcomScopeStatusBarLayoutTests
{
  [Theory]
  [InlineData(560, 96)]
  [InlineData(651, 96)]
  [InlineData(880, 96)]
  [InlineData(560, 120)]
  [InlineData(651, 144)]
  [InlineData(880, 192)]
  [InlineData(1200, 192)]
  public void StatusAndDiagnosticButtonShareSpacedUnclippedRow(int width, int dpi)
  {
    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try { RenderFixture(width, dpi); }
      catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
    if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
  }

  private static void RenderFixture(int width, int dpi)
  {
    int gap = (int)Math.Round(4 * dpi / 96.0);
    int contentHeight = (int)Math.Round(24 * dpi / 96.0);
    using var font = new Font("Segoe UI", 9f * dpi / 96f);
    using var root = new TableLayoutPanel
    {
      Size = new Size(width, 240), ColumnCount = 1, RowCount = 2,
      Padding = Padding.Empty, Margin = Padding.Empty, BackColor = Color.White
    };
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
    using var plot = new Panel
    {
      Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Navy
    };
    using var bar = new TableLayoutPanel
    {
      Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
      Padding = Padding.Empty, Margin = Padding.Empty, BackColor = Color.White
    };
    bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
    using var label = new Label
    {
      Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 0),
      TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
      Text = "Direct LAN · 29.9 帧/s · 控制 SkyCAT（已同步）", Font = font
    };
    using var button = new Button
    {
      Dock = DockStyle.Fill, Margin = Padding.Empty, Text = "诊断",
      FlatStyle = FlatStyle.Flat, Font = font
    };
    bar.Controls.Add(label, 0, 0);
    bar.Controls.Add(button, 1, 0);
    root.Controls.Add(plot, 0, 0);
    root.Controls.Add(bar, 0, 1);
    IcomScopeStatusBarLayout.Apply(root.RowStyles[1], bar, dpi);
    root.PerformLayout();
    bar.PerformLayout();

    Assert.Equal(plot.Bottom, bar.Top);
    Assert.Equal(gap, label.Top);
    Assert.Equal(gap, button.Top);
    Assert.Equal(contentHeight, label.Height);
    Assert.Equal(contentHeight, button.Height);
    Assert.True(label.Right <= button.Left);
    Assert.True(button.Right <= bar.ClientSize.Width);
    Assert.True(button.Bottom <= bar.ClientSize.Height);
    using var image = new Bitmap(root.Width, root.Height);
    root.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
    Assert.Equal(Color.Navy.ToArgb(), image.GetPixel(width / 2, plot.Bottom - 1).ToArgb());
    for (int y = bar.Top; y < bar.Top + gap; y++)
    {
      Assert.Equal(Color.White.ToArgb(), image.GetPixel(width / 2, y).ToArgb());
      Assert.Equal(Color.White.ToArgb(), image.GetPixel(width - 10, y).ToArgb());
    }
    string? dir = Environment.GetEnvironmentVariable("SKYROOF_SCOPE_LAYOUT_ARTIFACT_DIR");
    if (!string.IsNullOrWhiteSpace(dir))
    {
      Directory.CreateDirectory(dir);
      image.Save(Path.Combine(dir, $"scope-status-spacing-{width}px-dpi{dpi}.png"));
    }
  }
}
