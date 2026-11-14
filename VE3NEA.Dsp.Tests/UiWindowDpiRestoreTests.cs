using System;
using System.Drawing;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class UiWindowDpiRestoreTests
{
    [Theory]
    [InlineData(1600, 900, 96, 144, 2400, 1350)]
    [InlineData(1600, 900, 192, 96, 800, 450)]
    [InlineData(1280, 720, 120, 144, 1536, 864)]
    [InlineData(1401, 901, 96, 120, 1751, 1126)]
    [InlineData(1280, 720, 144, 144, 1280, 720)]
    [InlineData(1280, 720, 0, 144, 1280, 720)]
    public void RestoredWindowSizeScalesByPersistedDpi(
        int width, int height, int storedDpi, int currentDpi,
        int expectedWidth, int expectedHeight)
    {
        var actual = UiSettings.ScaleWindowSizeForDpi(
            new Size(width, height), storedDpi, currentDpi);
        Assert.Equal(new Size(expectedWidth, expectedHeight), actual);
    }

    [Fact]
    public void NewSettingsRetainLegacyDpiSemanticsUntilFirstSave()
    {
        var settings = new UiSettings { MainWindowSize = new Size(1280, 720) };
        Assert.Equal(0, settings.MainWindowDpi);
        Assert.Equal(settings.MainWindowSize, UiSettings.ScaleWindowSizeForDpi(
            settings.MainWindowSize, settings.MainWindowDpi, 192));
    }

    [Fact]
    public void DpiChangeScalesDimensionsRatherThanVirtualDesktopCoordinates()
    {
        var settings = new UiSettings
        {
            MainWindowLocation = new Point(-1700, 80),
            MainWindowSize = new Size(1100, 700),
            MainWindowDpi = 96
        };
        var size = UiSettings.ScaleWindowSizeForDpi(
            settings.MainWindowSize, settings.MainWindowDpi, 144);
        Assert.Equal(new Point(-1700, 80), settings.MainWindowLocation);
        Assert.Equal(new Size(1650, 1050), size);
    }
}
