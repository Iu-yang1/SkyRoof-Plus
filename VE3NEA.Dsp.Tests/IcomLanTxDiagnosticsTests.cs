using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class IcomLanTxDiagnosticsTests
  {
    private static readonly DateTime T0 =
      new(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShowsSeparateTransmitPcmRadioModAndFreshCivMeters()
    {
      var tx = new IcomDirectLanTxAudioStatus(
        true, true, 1236, -24, T0.Ticks,
        "DATA OFF=LAN, DATA=LAN, LAN MOD Level=50%; auto LAN source=ON",
        false, false, -7.5);
      var meters = new IcomMeterSnapshot(
        Power: new IcomMeterSample(0, T0),
        Alc: new IcomMeterSample(5, T0),
        Swr: new IcomMeterSample(0, T0));

      string diagnostics = IcomLanTxDiagnostics.Format(
        tx, true, meters, T0.AddMilliseconds(400));
      Assert.Contains("CAT PTT: ON", diagnostics);
      Assert.Contains("1236", diagnostics);
      Assert.Contains("RMS -24.0 dBFS", diagnostics);
      Assert.Contains("peak -7.5 dBFS", diagnostics);
      Assert.Contains("DATA OFF=LAN, DATA=LAN", diagnostics);
      Assert.Contains("LAN MOD Level=50%", diagnostics);
      Assert.Contains("PWR: 0% (CI-V raw 000", diagnostics);
      Assert.Contains("ALC: 4% (CI-V raw 005", diagnostics);
      Assert.Contains("UDP", diagnostics);
    }

    [Fact]
    public void ExplainsZeroLanLevelWithoutImplyingItCanKeyTheRadio()
    {
      var tx = new IcomDirectLanTxAudioStatus(
        true, true, 100, -18, T0.Ticks,
        "DATA OFF=LAN, DATA=LAN, LAN MOD Level=0%", false, true, -5);
      string description = IcomLanTxDiagnostics.Format(
        tx, true, new IcomMeterSnapshot(), T0);
      Assert.Contains("LAN MOD Level=0%", description);
      Assert.Contains("PWR:", description);
      Assert.Contains("ALC:", description);
      Assert.DoesNotContain("CI-V raw 000", description);
    }

    [Fact]
    public void OldMetersAndOldAudioNeverAppearAsLiveRfEvidence()
    {
      var tx = new IcomDirectLanTxAudioStatus(
        true, true, 150, -9, T0.AddSeconds(-4).Ticks);
      var old = new IcomMeterSnapshot(
        Power: new IcomMeterSample(0, T0.AddSeconds(-4)),
        Alc: new IcomMeterSample(0, T0.AddSeconds(-4)));
      string description = IcomLanTxDiagnostics.Format(tx, false, old, T0);
      Assert.Contains("CAT PTT: OFF", description);
      Assert.DoesNotContain("Sent PCM: RMS", description);
      Assert.Contains("PWR:", description);
      Assert.Contains("ALC:", description);
      Assert.DoesNotContain("CI-V raw 000", description);
    }
  }
}
