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
