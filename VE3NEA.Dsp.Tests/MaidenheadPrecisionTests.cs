using System;
using SkyRoof;
using SGPdotNET.CoordinateSystem;
using VE3NEA;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class MaidenheadPrecisionTests
{
    [Theory]
    [InlineData("OL79", 29.5, 115.0)]
    [InlineData("OL79WQ", 29.6875, 115.875)]
    [InlineData("OL79WQ12", 29.677083333333332, 115.84583333333333)]
    [InlineData("OL79WQ12AB", 29.675260416666667, 115.84184027777778)]
    public void LocatorConversionUsesFullPrecision(
        string grid, double expectedLatitude, double expectedLongitude)
    {
        GeoPoint location = GridSquare.ToGeoPoint(grid);
        Assert.Equal(expectedLatitude, location.Latitude, 8);
        Assert.Equal(expectedLongitude, location.Longitude, 8);
        Assert.Equal(grid[..4] + (grid.Length > 4 ? grid[4..].ToLowerInvariant() : ""),
            GridSquare.FromGeoPoint(location, grid.Length));
    }

    [Theory]
    [InlineData("OL79")]
    [InlineData("OL79WQ")]
    [InlineData("OL79WQ12")]
    [InlineData("OL79WQ12AB")]
    public void ValidLocatorsSupportCaseInsensitiveParsing(string grid)
    {
        Assert.True(GridSquare.IsValid(grid));
        Assert.True(GridSquare.IsValid(grid.ToLowerInvariant()));
        var location = GridSquare.ToGeoPoint(grid.ToLowerInvariant());
        Assert.Equal(GridSquare.ToGeoPoint(grid).Latitude, location.Latitude, 10);
        Assert.Equal(GridSquare.ToGeoPoint(grid).Longitude, location.Longitude, 10);
    }

    [Theory]
    [InlineData("RR73")]
    [InlineData("OL79W")]
    [InlineData("OL79WQ1")]
    [InlineData("OL79WQ12A")]
    [InlineData("OL79WQ12AY")]
    [InlineData("OL79WQ1299")]
    [InlineData("SL79WQ")]
    [InlineData("OL79WQ12ABCD")]
    public void RejectsInvalidLocators(string grid)
    {
        Assert.False(GridSquare.IsValid(grid));
        Assert.Throws<ArgumentException>(() => GridSquare.ToGeoPoint(grid));
    }

    [Fact]
    public void FullTenCharacterLocatorReachesTheOrbitGroundStation()
    {
        const string grid = "OL79WQ12AB";
        var station = SatellitePasses.CreateStationFromGridSquare(grid, 56);
        var geodetic = station.Location.ToGeodetic();
        var exactCenter = GridSquare.ToGeoPoint(grid);
        Assert.Equal(exactCenter.Latitude, geodetic.Latitude.Degrees, 7);
        Assert.Equal(exactCenter.Longitude, geodetic.Longitude.Degrees, 7);
        Assert.Equal(0.056, geodetic.Altitude, 7);
    }

    [Fact]
    public void TenCharacterLocatorIsNotTruncatedToSixCharacters()
    {
        GeoPoint coarse = GridSquare.ToGeoPoint("OL79WQ");
        GeoPoint precise = GridSquare.ToGeoPoint("OL79WQ12AB");
        Assert.True(Math.Abs(coarse.Latitude - precise.Latitude) > 0.001);
        Assert.True(Math.Abs(coarse.Longitude - precise.Longitude) > 0.001);
        Assert.Equal("OL79WQ12AB", GridSquare.FromGeoPoint(precise, 10).ToUpperInvariant());
    }

    [Fact]
    public void DeterministicWideAreaCoordinatesRoundTripForAllSupportedPrecisionLevels()
    {
        var random = new Random(20261113);
        for (int i = 0; i < 200; i++)
        {
            double latitude = -90 + random.NextDouble() * 180;
            double longitude = -180 + random.NextDouble() * 360;
            foreach (int length in new[] { 4, 6, 8, 10 })
            {
                string locator = GridSquare.FromGeoPoint(
                    new GeoPoint(latitude, longitude), length);
                Assert.True(GridSquare.IsValid(locator));
                Assert.Equal(locator,
                    GridSquare.FromGeoPoint(GridSquare.ToGeoPoint(locator), length));
            }
        }
    }

    [Fact]
    public void FourAndTenCharacterLocatorsPreserveCellCenterOnRoundTrip()
    {
        foreach (int length in new[] { 4, 6, 8, 10 })
        {
            string locator = GridSquare.FromGeoPoint(new GeoPoint(-32.346789, 151.247652), length);
            var center = GridSquare.ToGeoPoint(locator);
            Assert.Equal(locator, GridSquare.FromGeoPoint(center, length));
        }
    }
}
