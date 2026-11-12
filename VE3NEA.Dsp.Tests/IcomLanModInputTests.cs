using FluentAssertions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomLanModInputTests
  {
    [Theory]
    [InlineData(0x15)]
    [InlineData(0x16)]
    public void ReadBothModulationSelectorsWithoutAnyPttCommand(byte reg)
    {
      IcomLanModRegister register = (IcomLanModRegister)reg;
      IcomLanModCivCodec.BuildRead(0xA2, register)
        .Should().Equal(0xFE, 0xFE, 0xA2, 0xE0,
          0x1A, 0x05, 0x01, reg, 0xFD);
      IcomLanModCivCodec.BuildSourceWrite(0xA2, register, 5)
        .Should().Equal(0xFE, 0xFE, 0xA2, 0xE0,
          0x1A, 0x05, 0x01, reg, 0x05, 0xFD);
    }

    [Theory]
    [InlineData(0x15, 0x02, 2)]
    [InlineData(0x15, 0x05, 5)]
    [InlineData(0x16, 0x01, 1)]
    [InlineData(0x16, 0x05, 5)]
    public void RadioReadbackConfirmsActualPresetAndSource(
      byte register, byte value, int expected)
    {
      byte[] reply = [0xFE, 0xFE, 0xE0, 0xA2,
        0x1A, 0x05, 0x01, register, value, 0xFD];
      IcomLanModCivCodec.TryParse(reply, 0xA2, out var reading)
        .Should().BeTrue();
      reading.Value.Should().Be(expected);
      reading.Register.Should().Be((IcomLanModRegister)register);
    }

    [Theory]
    [InlineData(0x01, 0x28, 128)]
    [InlineData(0x00, 0x00, 0)]
    [InlineData(0x02, 0x55, 255)]
    public void ReadRadioLanModLevelAsRawBcdRelativeScale(
      byte hi, byte lo, int value)
    {
      byte[] reply = [0xFE, 0xFE, 0xE0, 0xA2,
        0x1A, 0x05, 0x01, 0x14, hi, lo, 0xFD];
      IcomLanModCivCodec.TryParse(reply, 0xA2, out var reading)
        .Should().BeTrue();
      reading.Value.Should().Be(value);
      reading.Register.Should().Be(IcomLanModRegister.LanLevel);
    }

    [Fact]
    public void RejectUnknownRadioMalformedBcdAndOutOfRangeSources()
    {
      byte[] reply = [0xFE, 0xFE, 0xE0, 0xA2,
        0x1A, 0x05, 0x01, 0x15, 0x05, 0xFD];
      reply[3] = 0xAC;
      IcomLanModCivCodec.TryParse(reply, 0xA2, out _).Should().BeFalse();
      reply[3] = 0xA2;
      reply[8] = 0x06;
      IcomLanModCivCodec.TryParse(reply, 0xA2, out _).Should().BeFalse();
      byte[] level = [0xFE, 0xFE, 0xE0, 0xA2,
        0x1A, 0x05, 0x01, 0x14, 0x01, 0xFA, 0xFD];
      IcomLanModCivCodec.TryParse(level, 0xA2, out _).Should().BeFalse();
    }

    [Fact]
    public void ManualModeWarnsAboutRadioModButDoesNotWriteAnything()
    {
      var state = new IcomLanModInputState();
      var reading = new IcomLanModReading(
        IcomLanModRegister.DataOffSource, 2);
      state.Apply(reading, autoSelectLan: false).Should().BeTrue();
      state.ConfirmedWrongSource.Should().BeTrue();
      state.Description.Should().Contain("DATA OFF=MIC/ACC");
      state.TryGetCorrection(reading, false, out _).Should().BeFalse();
    }

    [Fact]
    public void AutomaticLanSourceSelectsBothPresetsOnceAndConfirmsReadback()
    {
      var state = new IcomLanModInputState();
      IcomLanModReading normal = new(
        IcomLanModRegister.DataOffSource, 2);
      IcomLanModReading data = new(
        IcomLanModRegister.DataSource, 1);
      state.Apply(normal, true);
      state.TryGetCorrection(normal, true, out var toSet)
        .Should().BeTrue();
      toSet.Should().Be(IcomLanModRegister.DataOffSource);
      state.TryGetCorrection(normal, true, out _).Should().BeTrue();
      state.TryGetCorrection(normal, true, out _).Should().BeTrue();
      state.TryGetCorrection(normal, true, out _).Should().BeFalse();

      state.Apply(data, true);
      state.TryGetCorrection(data, true, out toSet).Should().BeTrue();
      toSet.Should().Be(IcomLanModRegister.DataSource);
      state.TryGetCorrection(data, true, out _).Should().BeTrue();
      state.TryGetCorrection(data, true, out _).Should().BeTrue();
      state.TryGetCorrection(data, true, out _).Should().BeFalse();

      var confirmedNormal = normal with { Value = 5 };
      var confirmedData = data with { Value = 5 };
      state.Apply(confirmedNormal, true);
      state.Apply(confirmedData, true);
      state.ConfirmedWrongSource.Should().BeFalse();
      state.Description.Should().Contain("DATA OFF=LAN, DATA=LAN");
      state.TryGetCorrection(confirmedNormal, true, out _)
        .Should().BeFalse();
    }

    [Fact]
    public void ZeroLanLevelIsVisibleWithoutInventingRfOutput()
    {
      var state = new IcomLanModInputState();
      state.Apply(new(IcomLanModRegister.LanLevel, 0), true);
      state.ConfirmedZeroLanLevel.Should().BeTrue();
      state.Description.Should().Contain("LAN MOD Level=0%");
      var status = new IcomDirectLanTxAudioStatus(
        true, true, 124, -17, DateTime.UtcNow.Ticks,
        state.Description, false, true);
      IcomDirectLanTxAudioDisplay.Format(status, true, DateTime.UtcNow)
        .Should().Contain("LAN MOD 0%");
    }

    [Fact]
    public void LocalTransmitLevelIsNotReportedAsRadioModConfirmation()
    {
      DateTime now = DateTime.UtcNow;
      var tx = new IcomDirectLanTxAudioStatus(
        true, true, 120, -17, now.Ticks,
        "DATA OFF=MIC/ACC, DATA=ACC", true);
      IcomDirectLanTxAudioDisplay.Format(tx, true, now)
        .Should().Contain("MOD≠LAN");
    }

    [Theory]
    [InlineData(true, true, 100, true)]
    [InlineData(true, false, 100, false)]
    [InlineData(false, true, 100, false)]
    [InlineData(true, true, 2000, false)]
    public void AutoModSwitchRequiresActualRecentPcAudioAndCatPtt(
      bool enabled, bool ptt, int audioAgeMs, bool expected)
    {
      long now = new DateTime(2026, 11, 12, 3, 0, 0,
        DateTimeKind.Utc).Ticks;
      long sent = now - TimeSpan.FromMilliseconds(audioAgeMs).Ticks;
      IcomLanModInputState.ShouldAutoSelect(
        enabled, ptt, sent, now).Should().Be(expected);
    }

    [Fact]
    public void AutomaticSelectionIsConfigurableWithoutChangingDigitalAgc()
    {
      var tx = new IcomDirectLanTransmitSettings();
      tx.AutoSelectLanModInput.Should().BeTrue();
      tx.Agc.Enabled.Should().BeFalse();
      tx.AutoSelectLanModInput = false;
      tx.Agc.Enabled.Should().BeFalse();
    }
  }
}
