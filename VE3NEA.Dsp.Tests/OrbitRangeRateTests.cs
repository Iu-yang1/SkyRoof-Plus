using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.TLE;
using SGPdotNET.Util;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class OrbitRangeRateTests
{
    private const string Tle1 =
        "1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753";
    private const string Tle2 =
        "2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667";

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(360)]
    public void CorrectedSgpNetRangeRateMatchesGeometricDistanceDerivative(int minutes)
    {
        var sat = new Satellite(new Tle(Tle1, Tle2));
        var station = new GroundStation(new GeodeticCoordinate(
            Angle.FromDegrees(28.5), Angle.FromDegrees(115.9), 0.04));
        DateTime epoch = new DateTime(2000, 6, 27, 18, 50, 19, DateTimeKind.Utc)
            .AddTicks(7_335_680);
        DateTime time = epoch.AddMinutes(minutes);
        var observation = SgpNetRangeRateCorrection.Observe(station, sat, time);
        var earlier = station.Observe(sat, time.AddSeconds(-1));
        var later = station.Observe(sat, time.AddSeconds(1));
        double finiteDifferenceMetresPerSecond = (later.Range - earlier.Range) * 500.0;

        Assert.InRange(
            Math.Abs(observation.RangeRate * 1000.0 - finiteDifferenceMetresPerSecond),
            0.0, 3.0);
        Assert.InRange(Math.Abs(observation.Range - station.Observe(sat, time).Range),
            0.0, 1e-9);
    }

    [Fact]
    public void PinnedSgpNet160OmitsEarthRotationVelocityInUncorrectedRangeRate()
    {
        var sat = new Satellite(new Tle(Tle1, Tle2));
        var station = new GroundStation(new GeodeticCoordinate(
            Angle.FromDegrees(28.5), Angle.FromDegrees(115.9), 0.04));
        DateTime time = new DateTime(2000, 6, 27, 18, 50, 19, DateTimeKind.Utc)
            .AddTicks(7_335_680);
        double raw = station.Observe(sat, time).RangeRate;
        double corrected = SgpNetRangeRateCorrection.Observe(station, sat, time).RangeRate;
        Assert.True(Math.Abs((raw - corrected) * 1000) > 100,
            $"Expected missing observer velocity in pinned SGP.NET 1.6.0, got {(raw-corrected)*1000:F3} m/s");
    }
}
