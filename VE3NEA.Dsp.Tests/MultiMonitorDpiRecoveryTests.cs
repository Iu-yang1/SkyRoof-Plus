using System;
using System.Drawing;
using System.Windows.Forms;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class MultiMonitorDpiRecoveryTests
{
  [Theory]
  [InlineData(-1600, 150, 1100, 700, 0, 150, 1100, 700)]
  [InlineData(2300, 80, 1280, 720, 640, 80, 1280, 720)]
  [InlineData(100, 50, 2600, 1600, 0, 0, 1920, 1040)]
  public void DisconnectedOrReducedMonitorRecoversVisibleWindow(
    int x, int y, int w, int h, int expectedX, int expectedY,
    int expectedWidth, int expectedHeight)
  {
    var primary = new Rectangle(0, 0, 1920, 1040);
    var recovered = UiMonitorPlacement.KeepWindowVisible(
      new Rectangle(x, y, w, h), new[] { primary });
    Assert.Equal(new Rectangle(
      expectedX, expectedY, expectedWidth, expectedHeight), recovered);
  }

  [Fact]
  public void WindowsSpanningMultipleMonitorsKeepTheirVirtualCoordinates()
  {
    var monitors = new[]
    {
      new Rectangle(-1920, 0, 1920, 1080),
      new Rectangle(0, 0, 1920, 1040)
    };
    var saved = new Rectangle(-1200, 150, 2400, 700);
    Assert.Equal(saved, UiMonitorPlacement.KeepWindowVisible(saved, monitors));
  }

  [Fact]
  public void NegativeCoordinateSecondaryDisplayKeepsAnAlreadyVisibleWindow()
  {
    var monitors = new[]
    {
      new Rectangle(-1920, 0, 1920, 1080),
      new Rectangle(0, 0, 1920, 1040)
    };
    var saved = new Rectangle(-1850, 75, 1440, 920);
    Assert.Equal(saved, UiMonitorPlacement.KeepWindowVisible(saved, monitors));
  }

  [Fact]
  public void WindowCanMoveToNonPrimaryScreenAfterHotplug()
  {
    var connected = new[] { new Rectangle(-2560, 0, 2560, 1400) };
    var old = new Rectangle(600, 100, 1500, 900);
    var actual = UiMonitorPlacement.KeepWindowVisible(old, connected);
    Assert.Equal(new Rectangle(-1500, 100, 1500, 900), actual);
  }

  [Theory]
  [InlineData(null, null, HighDpiMode.SystemAware)]
  [InlineData("PerMonitorV2", null, HighDpiMode.PerMonitorV2)]
  [InlineData("SystemAware", "--per-monitor-dpi", HighDpiMode.SystemAware)]
  [InlineData(null, "--per-monitor-dpi", HighDpiMode.PerMonitorV2)]
  [InlineData(null, "--system-dpi", HighDpiMode.SystemAware)]
  public void DpiAwarenessSelectionDoesNotChangeByAccident(
    string? env, string? arg, HighDpiMode expected)
  {
    var args = arg == null ? Array.Empty<string>() : new[] { arg };
    Assert.Equal(expected, Program.SelectDpiMode(args, env));
  }

  [Fact]
  public void FourDpiProfilesKeepExpectedWindowProportions()
  {
    foreach (int dpi in new[] { 96, 120, 144, 192 })
    {
      var original = new Size(1100, 700);
      var scaled = UiSettings.ScaleWindowSizeForDpi(original, 96, dpi);
      Assert.Equal((int)(original.Width * dpi / 96d), scaled.Width);
      Assert.Equal((int)(original.Height * dpi / 96d), scaled.Height);
    }
  }
}
