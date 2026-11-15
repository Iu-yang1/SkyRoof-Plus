using System;
using System.Linq;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class Unne1bEngineeringTests
{
    [Fact]
    public void RealPublishedGamePacket_MatchesNebrijaFieldLayout()
    {
        // Nebrija UNNE-1B registry's full example, without on-air CRC.
        byte[] packet = Convert.FromHexString(
            "AC00F51D0003000300000102000201");
        var fields = Unne1bTelemetry.Describe(packet)
            .ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("10", fields["Packet type"]);
        Assert.Equal("3", fields["Game week"]);
        Assert.Equal("0", fields["Game status"]);
        Assert.Equal("0300000102000201", fields["Game data (8 bytes)"]);
    }

    [Fact]
    public void PacketOne_ReadsTwelveBitEngineeringFields()
    {
        byte[] payload = new byte[29];
        payload[0] = 0x1C;
        payload[5] = 10;
        payload[8] = 20;
        // The first 12-bit VBUS sample is at bit 88.
        payload[11] = 0x12;
        payload[12] = 0x30;
        var fields = Unne1bTelemetry.Describe(payload)
            .ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("10 mW", fields["Panel A power"]);
        Assert.Equal("20 mW", fields["Panel D power"]);
        Assert.Equal("291 mV", fields["VBUS1"]);
    }

    [Fact]
    public void PacketThree_StatusBitsAndModeAreIndependent()
    {
        byte[] payload = new byte[27];
        payload[0] = 0x3C;
        payload[14] = 0x43;
        payload[15] = 0x12; // battery state 1, mode 2
        var fields = Unne1bTelemetry.Describe(payload)
            .ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("4", fields["Payload failures"]);
        Assert.Equal("3", fields["Last reset reason"]);
        Assert.Equal("FSK->FSK regenerative", fields["Transponder mode"]);
    }

    [Fact]
    public void TemperatureSentinelsAndRawSeriesAreHandled()
    {
        byte[] temp = new byte[15]; temp[0] = 0x2C;
        temp[5] = 0; temp[6] = 254; temp[7] = 255; temp[8] = 100;
        var fields = Unne1bTelemetry.Describe(temp).ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("<= -40 °C", fields["Temperature Panel A"]);
        Assert.Equal(">= 87 °C", fields["Temperature Panel B"]);
        Assert.Equal("Sensor error", fields["Temperature Panel C"]);
        Assert.Equal("10.0 °C", fields["Temperature Panel D"]);
        byte[] series = new byte[36]; series[0] = 0xEC; series[5] = 3;
        series[6] = 2; series[35] = 10;
        var time = Unne1bTelemetry.Describe(series).ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("CPU temperature", time["Time series variable"]);
        Assert.Equal("2 / 10", time["First / latest samples"]);
    }
}
