using SkyRoof.Remote.Station;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class StationAfcProbeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ProbeRequiresMatchingCatBiasAndRestoredIfBeforeConfirmingSign(int sign)
    {
        var t = DateTime.UtcNow;
        var probe = new StationAfcPolarityProbe(2000, t);
        probe.Readback(true, false, "USB", 0, t);
        Assert.False(probe.Observe(100, 1, .99, true, false, true, 1, t.AddMilliseconds(700)));
        Assert.Equal(150, probe.Bias);
        probe.Readback(true, false, "USB", 0, t.AddSeconds(1));
        Assert.False(probe.Observe(100 - sign * 150, 1, .99, true, false, true, 1, t.AddSeconds(2)));
        Assert.Equal(150, probe.Bias);
        probe.Readback(true, false, "USB", 150, t.AddSeconds(2));
        Assert.False(probe.Observe(100 - sign * 150, 1, .99, true, false, true, 1, t.AddSeconds(3)));
        Assert.Equal(0, probe.Bias);
        Assert.Equal(0, probe.Sign);
        probe.Readback(true, false, "USB", 0, t.AddSeconds(4));
        Assert.True(probe.Observe(104, 1, .99, true, false, true, 1, t.AddSeconds(5)));
        Assert.True(probe.Verified);
        Assert.Equal(sign, probe.Sign);
    }

    [Fact]
    public void TransmitAndTimeoutRestoreNominalWithoutCertifyingPolarity()
    {
        var t = DateTime.UtcNow;
        var probe = new StationAfcPolarityProbe(100, t);
        probe.Readback(true, false, "USB", 0, t);
        probe.Observe(100, 1, .99, true, false, true, 1, t.AddSeconds(1));
        Assert.Equal(100, probe.Bias);
        probe.Readback(true, true, "USB", 100, t.AddSeconds(2));
        Assert.Equal(0, probe.Bias);
        Assert.Equal("AbortRestore", probe.State);
        probe.Readback(true, false, "USB", 0, t.AddSeconds(3));
        Assert.Equal("Failed", probe.State);
        Assert.Equal(0, probe.Sign);
        probe.Restart(t.AddSeconds(4));
        probe.Tick(t.AddSeconds(20));
        Assert.Equal("AbortRestore", probe.State);
        Assert.Equal(0, probe.Bias);
    }
}
