using FluentAssertions;
using Xunit;
using SkyRoof;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomDirectLanMeterCodecTests
  {
    [Theory]
    [InlineData(0x02, 0x01, 0x20, 120)]
    [InlineData(0x11, 0x02, 0x13, 213)]
    [InlineData(0x12, 0x00, 0x80, 80)]
    [InlineData(0x13, 0x00, 0x48, 48)]
    public void ParsesCorrelatedBcdLevels(byte cmd, byte hi, byte lo, int expected)
    {
      byte[] frame = [0xFE, 0xFE, 0xE0, 0xA2, 0x15, cmd, hi, lo, 0xFD];
      IcomMeterCivCodec.TryParse(frame, 0xA2, out var reading).Should().BeTrue();
      reading.Raw.Should().Be(expected);
      IcomMeterCivCodec.BuildRead(0xA2, reading.Kind)
        .Should().Equal(0xFE, 0xFE, 0xA2, 0xE0, 0x15, cmd, 0xFD);
    }

    [Fact]
    public void AcceptsRadioMeterBroadcastReadbackWhenRadioAddressMatches()
    {
      // The IC-9700 may direct its response to CI-V controller E0 or
      // broadcast 00; the sender address and matching command remain fixed.
      byte[] frame = [0xFE, 0xFE, 0x00, 0xA2, 0x15, 0x02,
                      0x01, 0x20, 0xFD];
      IcomMeterCivCodec.TryParse(frame, 0xA2, out var reading)
        .Should().BeTrue();
      reading.Raw.Should().Be(120);
      frame[3] = 0xA4;
      IcomMeterCivCodec.TryParse(frame, 0xA2, out _)
        .Should().BeFalse();
    }

    [Fact]
    public void RejectsWrongSenderAndInvalidBcd()
    {
      byte[] frame = [0xFE, 0xFE, 0xE0, 0xA2, 0x15, 0x02, 0x01, 0x20, 0xFD];
      IcomMeterCivCodec.TryParse(frame, 0x94, out _).Should().BeFalse();
      frame[7] = 0xA0;
      IcomMeterCivCodec.TryParse(frame, 0xA2, out _).Should().BeFalse();
      frame[7] = 0x20;
      frame[5] = 0x99;
      IcomMeterCivCodec.TryParse(frame, 0xA2, out _).Should().BeFalse();
    }

    [Fact]
    public void UsesIcomScaleInsteadOfLinearWatts()
    {
      IcomMeterCivCodec.Display(IcomMeterKind.S, 120).Should().Be("S9");
      IcomMeterCivCodec.Display(IcomMeterKind.S, 241).Should().Be("S9+60");
      IcomMeterCivCodec.PowerPercent(143).Should().BeApproximately(50, 0.01);
      IcomMeterCivCodec.PowerPercent(213).Should().BeApproximately(100, 0.01);
      IcomMeterCivCodec.SwrRatio(48).Should().BeApproximately(1.5, 0.001);
      IcomMeterCivCodec.SwrRatio(120).Should().BeApproximately(3, 0.001);
    }
  }
}
