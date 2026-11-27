using SkyRoof.RemoteDesktop;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class EarthHorizonTests
{
    [Theory]
    [InlineData(0, 0, 400000)]
    [InlineData(52, 179.9, 1200000)]
    [InlineData(90, 30, 400000)]
    [InlineData(-85, -179.9, 35786000)]
    public void FootprintPointsAreTangentToWgs84(double lat, double lon, double height)
    {
        var outline = EarthHorizon.Outline(lat, lon, height);
        Assert.Equal(181, outline.Count);
        var satellite = Ecef(lat, lon, height);
        foreach (var (pointLat, pointLon) in outline)
        {
            Assert.InRange(pointLat, -90, 90);
            Assert.InRange(pointLon, -180, 180);
            var surface = Ecef(pointLat, pointLon, 0);
            var normal = EcefNormal(pointLat, pointLon);
            double projection = (satellite.X - surface.X) * normal.X +
                (satellite.Y - surface.Y) * normal.Y + (satellite.Z - surface.Z) * normal.Z;
            Assert.InRange(Math.Abs(projection), 0, 0.001);
        }
        Assert.InRange(Math.Abs(outline[0].Lat - outline[^1].Lat), 0, 1e-8);
        Assert.InRange(Math.Abs(RemoteEarthMap.Wrap(outline[0].Lon - outline[^1].Lon)), 0, 1e-8);
    }

    [Fact]
    public void EquatorialHorizonMatchesAnalyticLongitude()
    {
        var outline = EarthHorizon.Outline(0, 0, 400000);
        double expected = Math.Acos(6378137.0 / 6778137.0) * 180 / Math.PI;
        Assert.Equal(0, outline[0].Lat, 8);
        Assert.Equal(expected, outline[0].Lon, 8);
        Assert.Empty(EarthHorizon.Outline(0, 0, 0));
    }

    private static (double X, double Y, double Z) Ecef(double lat, double lon, double h)
    {
        const double a = 6378137, e2 = 0.0066943799901413165;
        var normal = EcefNormal(lat, lon);
        double n = a / Math.Sqrt(1 - e2 * normal.Z * normal.Z);
        return ((n + h) * normal.X, (n + h) * normal.Y, (n * (1 - e2) + h) * normal.Z);
    }

    private static (double X, double Y, double Z) EcefNormal(double lat, double lon)
    {
        lat *= Math.PI / 180;
        lon *= Math.PI / 180;
        return (Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }
}
