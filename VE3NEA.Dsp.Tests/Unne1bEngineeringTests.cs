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
        // Official AMSAT-EA packed x86 packet structures store uint32_t
        // in little-endian: 00 F5 1D 00 => 1,963,264, not 16,063,744.
        Assert.Equal("1963264", fields["Satellite clock (seconds since boot)"]);
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
        // AMSAT-EA v1.14 power values are 2 mW/LSB, and SPI is LE.
        payload[9] = 0x34;
        payload[10] = 0x12;
        // Packed LE word 0x3012: VBUS1 = (0x3012 >> 4) * 1400 / 1000.
        payload[11] = 0x12;
        payload[12] = 0x30;
        var fields = Unne1bTelemetry.Describe(payload)
            .ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("20 mW", fields["Panel A power"]);
        Assert.Equal("40 mW", fields["Panel D power"]);
        Assert.Equal("9320 mW", fields["Instant panel power"]);
        Assert.Equal("1076 mV", fields["VBUS1"]);
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
    public void StatusUsesNativeLittleEndianUptimeAndCpuRunCounters()
    {
        byte[] payload = new byte[27];
        payload[0] = 0x3C;
        payload[1] = 0x10; // sclock = 0x00000010
        payload[5] = 0x78; payload[6] = 0x56;
        payload[7] = 0x34; payload[8] = 0x12;
        payload[9] = 0xFE; payload[10] = 0x01;
        var fields = Unne1bTelemetry.Describe(payload).ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("16", fields["Satellite clock (seconds since boot)"]);
        Assert.Equal("305419896 s", fields["Uptime"]);
        Assert.Equal("510", fields["CPU runs"]);
    }

    [Fact]
    public void PowerUsesSignedTwelveBitCurrentsAndHalfDbLevels()
    {
        byte[] payload = new byte[29];
        payload[0] = 0x1C;
        // IBAT bits 11..0 = 0xFFF (minus 1 mA).
        payload[19] = 0x0F;
        payload[22] = 0xFF;
        // Payload I = 0xFFC (minus 4 mA).
        payload[23] = 0xFC;
        payload[24] = 0x0F;
        payload[25] = 3; payload[26] = 4;
        var fields = Unne1bTelemetry.Describe(payload).ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("-1 mA", fields["IBAT"]);
        Assert.Equal("-4 mA", fields["Payload current"]);
        Assert.Equal("1.5 dBm", fields["Peak signal"]);
        Assert.Equal("2.0 dBm", fields["Mean noise"]);
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
