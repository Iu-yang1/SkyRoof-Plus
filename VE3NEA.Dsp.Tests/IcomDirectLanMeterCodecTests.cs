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
    public void MeterPollingPrioritizesReceiverSAndTransmitPower()
    {
      var options = new IcomDirectLanMeterSettings();
      for (int i = 0; i < 12; i++)
        IcomMeterPollingPolicy.Choose(options, false, i)
          .Should().Be(IcomMeterKind.S);

      var tx = Enumerable.Range(0, 6)
        .Select(i => IcomMeterPollingPolicy.Choose(options, true, i))
        .ToArray();
      tx.Should().Equal(
        IcomMeterKind.Power, IcomMeterKind.Alc,
        IcomMeterKind.Power, IcomMeterKind.Alc,
        IcomMeterKind.Power, IcomMeterKind.Swr);

      options.Power = false;
      options.Alc = false;
      IcomMeterPollingPolicy.Choose(options, true, 8)
        .Should().Be(IcomMeterKind.Swr);
      options.Swr = false;
      IcomMeterPollingPolicy.Choose(options, true, 0)
        .Should().BeNull();
    }

    [Fact]
    public void MeterDisplayDoesNotMixReceiveSAndTransmitMeters()
    {
      DateTime now = new DateTime(2026, 11, 11, 14, 13, 0, DateTimeKind.Utc);
      IcomMeterSnapshot values = new IcomMeterSnapshot()
        .With(new IcomMeterReading(IcomMeterKind.S, 120), now)
        .With(new IcomMeterReading(IcomMeterKind.Power, 143), now);
      var rx = IcomMeterPollingPolicy.VisibleForMode(values, false);
      rx.S.Should().NotBeNull();
      rx.Power.Should().BeNull();
      rx.PowerPeak.Should().BeNull();
      var tx = IcomMeterPollingPolicy.VisibleForMode(values, true);
      tx.S.Should().BeNull();
      tx.Power.Should().NotBeNull();
    }

    [Fact]
    public void ShortSsBPwrPeakIsHeldOnlyFromActualRadioReading()
    {
      DateTime now = new DateTime(2026, 11, 11, 14, 13, 0, DateTimeKind.Utc);
      IcomMeterSnapshot values = new IcomMeterSnapshot()
        .With(new IcomMeterReading(IcomMeterKind.Power, 143), now)
        .With(new IcomMeterReading(IcomMeterKind.Power, 0),
          now.AddMilliseconds(500));
      var (held, isHeld) = values.DisplayPower(now.AddMilliseconds(500));
      isHeld.Should().BeTrue();
      held?.Raw.Should().Be(143);

      values = values.With(new IcomMeterReading(IcomMeterKind.Power, 0),
        now.AddMilliseconds(1700));
      var (expired, expiredHeld) = values.DisplayPower(now.AddMilliseconds(1700));
      expiredHeld.Should().BeFalse();
      expired?.Raw.Should().Be(0);

      // ALC alone must never imply that RF power has been measured.
      var alcOnly = new IcomMeterSnapshot()
        .With(new IcomMeterReading(IcomMeterKind.Alc, 100), now);
      alcOnly.DisplayPower(now).Sample.Should().BeNull();
    }

    [Theory]
    [InlineData(false, 0L, true)]
    [InlineData(true, 500L, false)]
    [InlineData(true, 1199L, false)]
    [InlineData(true, 1200L, true)]
    public void MeterReadsRespectUrgentDopplerAndPttBudget(
      bool urgent, long milliseconds, bool expected)
    {
      CatControlEngine.MayPollMeter(urgent, milliseconds).Should().Be(expected);
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
