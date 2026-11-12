using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class OrbitSourceAndParserTests
{
    [Fact]
    public void CustomTleParser_AcceptsTwoLineAndThreeLineRecords()
    {
      const string line1 =
        "1 25544U 98067A   24001.50000000  .00000000  00000-0  00000-0 0  9999";
      const string line2 =
        "2 25544  51.6400 100.0000 0005000 200.0000 160.0000 15.50000000123456";

      var twoLine =
        SatnogsDb.TlesFromText(
          $"{line1}\n{line2}",
          "Test");

      var threeLine =
        SatnogsDb.TlesFromText(
          $"ISS\n{line1}\n{line2}",
          "Test");

      twoLine.Should().HaveCount(1);
      threeLine.Should().HaveCount(1);
      twoLine[0].norad_cat_id.Should().Be(25544);
      threeLine[0].tle0.Should().Be("ISS");
      threeLine[0].tle_source.Should().Be("Test");
    }

    [Fact]
    public void CustomTleSources_AcceptSemicolonAndNewlineLists()
    {
      string[] sources =
        SatnogsDb.SplitCustomTleSources(
          "https://example.invalid/a.txt;C:\\tle\\local.txt\nhttps://example.invalid/a.txt");

      sources.Should().Equal(
        "https://example.invalid/a.txt",
        "C:\\tle\\local.txt");
    }

    [Fact]
    public void CustomTleSources_FirstEntryHasHighestPriority()
    {
      SatnogsDb.GetCustomTleApplyOrder(
          "first.txt;second.txt;third.txt")
        .Should().Equal(
          "third.txt",
          "second.txt",
          "first.txt");
    }

    [Fact]
    public void OrbitEngineDefaultsToOrekitAndDownloadsItsData()
    {
        var settings = new OrbitSourceSettings();
        settings.Backend.Should().Be(OrbitEngineMode.Orekit);
        settings.AutoDownloadOrekitData.Should().BeTrue();
        settings.ShowSolarSystemTargets.Should().BeTrue();
        settings.SatellitesUrl.Should().Be(OrbitSourceSettings.DefaultSatellitesUrl);
        settings.TransmittersUrl.Should().Be(OrbitSourceSettings.DefaultTransmittersUrl);
        settings.TleUrl.Should().Be(OrbitSourceSettings.DefaultTleUrl);
    }
}
