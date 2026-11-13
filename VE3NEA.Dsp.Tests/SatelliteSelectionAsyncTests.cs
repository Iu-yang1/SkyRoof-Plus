using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class SatelliteSelectionAsyncTests
{
    [Fact]
    public void LatestSatellitePredictionMayUpdateTheSelector()
    {
        var satellite = new SatnogsDbSatellite { sat_id = "SAT-A" };
        Assert.True(SatelliteSelectorWidget.ShouldApplyNextPass(
            7, 7, satellite, satellite, operatorSelectedPass: false));
    }

    [Theory]
    [InlineData(false, false, 30, true, false)]
    [InlineData(true, false, 30, true, false)]
    [InlineData(true, true, 30, true, false)]
    [InlineData(true, false, 121, false, false)]
    [InlineData(true, true, 30, false, true)]
    public void NextPassCacheFreshnessAlsoCachesNoPassResult(
        bool ready, bool hasPass, int ageSeconds,
        bool expected, bool expiredPass)
    {
        var now = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        DateTime? passEnd = hasPass
            ? (expiredPass ? now.AddSeconds(-1) : now.AddMinutes(20))
            : null;
        Assert.Equal(expected, SatellitePasses.IsNextPassCacheFresh(
            now, now.AddSeconds(-ageSeconds), ready, passEnd));
    }

    [Fact]
    public void StaleOrUnrelatedSatellitePredictionCannotOverwriteSelection()
    {
        var first = new SatnogsDbSatellite { sat_id = "SAT-A" };
        var second = new SatnogsDbSatellite { sat_id = "SAT-B" };
        Assert.False(SatelliteSelectorWidget.ShouldApplyNextPass(
            6, 7, first, first, operatorSelectedPass: false));
        Assert.False(SatelliteSelectorWidget.ShouldApplyNextPass(
            7, 7, first, second, operatorSelectedPass: false));
        Assert.False(SatelliteSelectorWidget.ShouldApplyNextPass(
            7, 7, first, first, operatorSelectedPass: true));
    }
}
