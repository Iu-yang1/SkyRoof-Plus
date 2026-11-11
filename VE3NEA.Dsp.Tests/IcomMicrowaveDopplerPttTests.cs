using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomMicrowaveDopplerPttTests
  {
    [Fact]
    public void TenGigahertzUsesPhysicalLightSpeedNotApproximation()
    {
      double rangeRateKmSec = 7.5;
      double exact = 10_368_100_000.0 * rangeRateKmSec /
        RadioLink.SpeedOfLightKmPerSecond;
      double approximate = 10_368_100_000.0 * rangeRateKmSec / 300_000;
      Assert.InRange(exact - approximate, 170, 200);
    }

    [Fact]
    public void DopplerCorrectionsRemainDistinctAt10368And5760MHz()
    {
      var link = new RadioLink
      {
        IsTerrestrial = false,
        Tx = new SatnogsDbTransmitter
        {
          downlink_low = 10_368_100_000,
          uplink_low = 5_760_100_000
        },
        SatCust = new SatelliteCustomization(),
        TxCust = new TransmitterCustomization(),
        DopplerFactor = 7.5 / RadioLink.SpeedOfLightKmPerSecond,
        RitEnabled = true,
        RitOffset = 75,
        XitOffset = 125
      };

      link.DownlinkDopplerCorrectionEnabled = true;
      link.UplinkDopplerCorrectionEnabled = true;
      link.DownlinkManualCorrectionEnabled = false;
      link.UplinkManualCorrectionEnabled = false;
      link.ComputeFrequencies();

      double expectedDown = (10_368_100_000.0 + 75) *
        (1 - link.DopplerFactor);
      double expectedUp = 5_760_100_000.0 *
        (1 + link.DopplerFactor) + 125;
      Assert.InRange(Math.Abs(expectedDown - link.CorrectedDownlinkFrequency),
        0, 0.001);
      Assert.InRange(Math.Abs(expectedUp - link.CorrectedUplinkFrequency),
        0, 0.001);
      Assert.True(link.HasUplink);
      Assert.True(link.IsCrossBand);
    }

    [Theory]
    [InlineData(145_900_004.9, 10, 145_900_000L)]
    [InlineData(435_600_009.8, 10, 435_600_000L)]
    [InlineData(1_296_100_003.7, 1, 1_296_100_004L)]
    [InlineData(5_760_100_000.6, 1, 5_760_100_001L)]
    [InlineData(10_368_100_009.8, 1, 10_368_100_010L)]
    public void CatUses64BitMicrowaveHzQuantization(
      double frequencyHz, int microwaveStep, long expectedHz)
    {
      Assert.Equal(expectedHz,
        CatControlEngine.QuantizeCatFrequency(
          frequencyHz, 10, microwaveStep));
    }

    [Fact]
    public void OperatorMayChooseAlternateMicrowaveTuningStep()
    {
      Assert.Equal(10_368_100_010L,
        CatControlEngine.QuantizeCatFrequency(
          10_368_100_014.2, 10, 10));
      Assert.Equal(10_368_100_014L,
        CatControlEngine.QuantizeCatFrequency(
          10_368_100_014.2, 100, 1));
    }

    [Theory]
    [InlineData(500, 100, 145_900_000L, 435_600_000L, 500)]
    [InlineData(500, 100, 1_296_100_000L, 435_600_000L, 100)]
    [InlineData(500, 100, 435_600_000L, 10_368_100_000L, 100)]
    [InlineData(50, 100, 10_368_100_000L, 0L, 50)]
    [InlineData(500, 500, 10_368_100_000L, 0L, 500)]
    public void MicrowaveCatCycleIsFasterWithoutChangingVhfUhfPacing(
      int legacyDelay, int microwaveDelay, long rx, long tx, int expected)
    {
      Assert.Equal(expected, CatControlEngine.SelectCatCycleDelayMs(
        legacyDelay, microwaveDelay, rx, tx));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    public void PttOffIsUnconditionalButOnWaitsForSupportedTxWrites(
      bool requestedOn, bool ctcssReady, bool prepared, bool shouldAttempt)
    {
      Assert.Equal(shouldAttempt,
        CatControlEngine.CanAttemptPttWithPreparedTx(
          requestedOn, ctcssReady, prepared));
    }

    [Fact]
    public void SingleRadioCannotBeAssumedCrossBandDuplexWithoutCapabilities()
    {
      var caps = new RadioCapabilities
      {
        model = "IC-905",
        cross_band_split = false,
        split = new AvailableCommands
        {
          when_receiving = [CatAction.write_rx_frequency],
          when_transmitting = [CatAction.write_tx_frequency]
        }
      };
      Assert.True(caps.CanSplitTune(false));
      Assert.False(caps.CanSplitTune(true));
    }

    [Fact]
    public void IndependentRadioCatPortsKeepRxAndTxSeparate()
    {
      var rx = new CatRadioSettings
      {
        Enabled = true,
        Host = "127.0.0.1",
        Port = 4532
      };
      var tx = new CatRadioSettings
      {
        Enabled = true,
        Host = "127.0.0.1",
        Port = 4533
      };
      Assert.False(CatControl.IsSameHostPort(tx, rx));
    }
  }
}
