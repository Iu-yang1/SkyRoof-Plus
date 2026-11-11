using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomMicrowaveSatelliteBandTests
  {
    [Theory]
    [InlineData(52_000_000d, (int)AmateurRadioBand.SixMeters)]
    [InlineData(145_900_000d, (int)AmateurRadioBand.TwoMeters)]
    [InlineData(435_600_000d, (int)AmateurRadioBand.SeventyCentimeters)]
    [InlineData(1_296_100_000d, (int)AmateurRadioBand.TwentyThreeCentimeters)]
    [InlineData(2_400_100_000d, (int)AmateurRadioBand.ThirteenCentimeters)]
    [InlineData(5_760_100_000d, (int)AmateurRadioBand.FiveCentimeters)]
    [InlineData(10_368_100_000d, (int)AmateurRadioBand.ThreeCentimeters)]
    [InlineData(900_000_000d, (int)AmateurRadioBand.Unknown)]
    [InlineData(2_800_000_000d, (int)AmateurRadioBand.Unknown)]
    public void IdentifiesSatelliteAndMicrowaveAmateurBand(
      double frequencyHz, int bandId)
    {
      AmateurRadioBand band = (AmateurRadioBand)bandId;
      Assert.Equal(band, SatnogsDbTransmitter.GetAmateurBand(frequencyHz));
      Assert.Equal(band != AmateurRadioBand.Unknown,
        SatnogsDbTransmitter.IsHamFrequency(frequencyHz));
    }

    [Theory]
    [InlineData(144_000_000d, (int)AmateurRadioBand.TwoMeters)]
    [InlineData(148_000_000d, (int)AmateurRadioBand.TwoMeters)]
    [InlineData(430_000_000d, (int)AmateurRadioBand.SeventyCentimeters)]
    [InlineData(440_000_000d, (int)AmateurRadioBand.SeventyCentimeters)]
    [InlineData(1_240_000_000d, (int)AmateurRadioBand.TwentyThreeCentimeters)]
    [InlineData(1_300_000_000d, (int)AmateurRadioBand.TwentyThreeCentimeters)]
    [InlineData(2_300_000_000d, (int)AmateurRadioBand.ThirteenCentimeters)]
    [InlineData(2_450_000_000d, (int)AmateurRadioBand.ThirteenCentimeters)]
    [InlineData(5_650_000_000d, (int)AmateurRadioBand.FiveCentimeters)]
    [InlineData(5_925_000_000d, (int)AmateurRadioBand.FiveCentimeters)]
    [InlineData(10_000_000_000d, (int)AmateurRadioBand.ThreeCentimeters)]
    [InlineData(10_500_000_000d, (int)AmateurRadioBand.ThreeCentimeters)]
    public void KeepsBandEdgesInclusive(
      double frequencyHz, int expectedBandId)
    {
      Assert.Equal((AmateurRadioBand)expectedBandId,
        SatnogsDbTransmitter.GetAmateurBand(frequencyHz));
    }

    [Theory]
    [InlineData(145_900_000d, 435_600_000d, true)]
    [InlineData(435_600_000d, 1_296_100_000d, true)]
    [InlineData(1_296_100_000d, 2_400_100_000d, true)]
    [InlineData(2_400_100_000d, 2_404_000_000d, false)]
    [InlineData(5_760_100_000d, 10_368_100_000d, true)]
    [InlineData(10_368_100_000d, 10_450_000_000d, false)]
    public void RadioLinkDetectsCrossBandAtMicrowaveFrequencies(
      double downlink, double uplink, bool expectedCrossBand)
    {
      var link = new RadioLink
      {
        IsTerrestrial = false,
        Tx = new SatnogsDbTransmitter
        {
          downlink_low = (long)downlink
        },
        DownlinkFrequency = downlink,
        UplinkFrequency = uplink
      };

      Assert.True(link.HasDownlink);
      Assert.True(link.HasUplink);
      Assert.Equal(expectedCrossBand, link.IsCrossBand);
    }

    [Fact]
    public void FrequencyBandClassificationIsNotTransmissionAuthorization()
    {
      // True means "recognized band" only; regulatory status depends on
      // country, licence class, satellite direction and the selected radio.
      Assert.True(SatnogsDbTransmitter.IsHamFrequency(1_296_100_000d));
      Assert.True(SatnogsDbTransmitter.IsHamFrequency(10_368_100_000d));
    }
  }
}
