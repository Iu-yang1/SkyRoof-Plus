using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class Funcube1TelemetryTests
{
    [Fact]
    public void FlightFrameHasCorrectTypeRawEpsCountersAndStatusBits()
    {
        var frame = new byte[256];
        frame[0] = (byte)((2 << 6) | 9); // FC1 flight; WO10
        frame[1] = 0x12; frame[2] = 0x34;
        frame[7] = 0x00; frame[8] = 0x9c;
        frame[9] = 0x09; frame[10] = 0x82;
        frame[13] = 0x00; frame[14] = 0x03;
        frame[15] = 0x01; frame[16] = 0x02;
        frame[50] = 0x01; frame[51] = 0x23; frame[52] = 0x45;
        frame[55] = 0x18; // eclipse / safe mode set
        var fields = Funcube1Telemetry.Describe(frame)
            .ToDictionary(x => x.Name, x => x.Value);

        Assert.Equal("FUNcube-1 (flight)", fields["Satellite"]);
        Assert.Equal("WO10", fields["Frame type"]);
        Assert.Equal("0x89", fields["Frame header"]);
        Assert.Equal("4660", fields["Solar panel 1 voltage (raw)"]);
        Assert.Equal("156", fields["Solar photocurrent (raw)"]);
        Assert.Equal("2434", fields["Battery voltage (raw)"]);
        Assert.Equal("3", fields["Reboot count"]);
        Assert.Equal("258", fields["Software error count"]);
        Assert.Equal("74565", fields["Sequence number"]);
        Assert.Equal("Yes", fields["Eclipse"]);
        Assert.Equal("Yes", fields["Safe mode"]);
    }

    [Fact]
    public void WrongFrameSizeAndOtherFuncubeModelsCannotBeMisreadAsFc1Eps()
    {
        Assert.Empty(Funcube1Telemetry.Describe(new byte[255]));
        var frame = new byte[256];
        frame[0] = (byte)((1 << 6) | 2); // FC2 has a different EPS structure
        var fields = Funcube1Telemetry.Describe(frame)
            .ToDictionary(x => x.Name, x => x.Value);
        Assert.Equal("FUNcube-2", fields["Satellite"]);
        Assert.Equal("WO3", fields["Frame type"]);
        Assert.DoesNotContain("Battery voltage (raw)", fields.Keys);
        Assert.DoesNotContain("Sequence number", fields.Keys);

        frame[0] = (byte)((3 << 6) | 13);
        var extended = Funcube1Telemetry.Describe(frame)
            .ToDictionary(x => x.Name, x => x.Value);
        Assert.Equal("Extended (13)", extended["Frame type"]);
        Assert.DoesNotContain("Battery voltage (raw)", extended.Keys);
    }
}
