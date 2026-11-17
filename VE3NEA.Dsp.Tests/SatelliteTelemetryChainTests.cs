using MathNet.Numerics;
using SkyRoof;
using SkyRoof.Satellites;
using VE3NEA;
using VE3NEA.SkyTlm.Core;
using Xunit;

namespace VE3NEA.Dsp.Tests;

/// <summary>Regression for metadata-driven satellite receive paths and pooled IQ blocks.</summary>
public sealed class SatelliteTelemetryChainTests
{
    [Theory]
    [InlineData("AO-40 FEC", null, true)]
    [InlineData("AO-40 FEC", "differential", true)]
    [InlineData("AO-40 FEC", "none", false)]
    [InlineData("CCSDS Reed-Solomon", null, false)]
    public void Ao40FecDefaultsToDbpsk_WhileExplicitPrecodingWins(
        string framing, string? precoding, bool expected)
    {
        var sat = new SatnogsDbSatellite
        {
            sat_id = "FUNCUBE-1", norad_cat_id = 39444, name = "AO-73"
        };
        var tx = new SatnogsDbTransmitter
        {
            uuid = "ao73-telemetry",
            norad_cat_id = 39444,
            description = "1k2 BPSK telemetry",
            mode = "BPSK", DownlinkMode = "USB",
            downlink_low = 145935000, baud = 1200,
            gr_sats = new GrSatsInfo
            {
                baudrate = 1200, modulation = "BPSK",
                framing = framing, precoding = precoding
            },
            Satellite = sat
        };

        var result = SignalParamsResolver.Resolve(tx);
        Assert.NotNull(result);
        Assert.Equal(1200, result!.Baud);
        Assert.Equal(Modulation.BPSK, result.Modulation);
        Assert.Equal(expected, result.Differential);
        Assert.True(SignalParamsResolver.IsTelemetryDecodable(result));
    }

    [Fact]
    public void Ao73SatyamlWithoutPrecodingStillResolvesToAo40FecDbpsk()
    {
        var tx = new SatnogsDbTransmitter
        {
            uuid = "ao73",
            norad_cat_id = 39444,
            description = "1k2 BPSK telemetry beacon",
            mode = "BPSK", DownlinkMode = "USB",
            downlink_low = 145935000, baud = 1200,
            gr_sats = new GrSatsInfo
            {
                baudrate = 1200, modulation = "DBPSK",
                framing = "AO-40 FEC"
            }
        };
        var p = SignalParamsResolver.Resolve(tx);
        Assert.NotNull(p);
        Assert.Equal(Framing.AO40FEC, p!.Framing);
        Assert.True(p.Differential);
        Assert.NotEqual(true, p.Manchester);
    }

    [Fact]
    public void Ao73BeaconCanReceiveWithoutSatyamlEnrichment()
    {
        var noSatyaml = new SatnogsDbTransmitter
        {
            uuid = "ao73-beacon", norad_cat_id = 39444,
            downlink_low = 145935000, baud = 1200,
            description = "1k2 BPSK telemetry",
            mode = "BPSK", DownlinkMode = "USB"
        };
        var result = SignalParamsResolver.Resolve(noSatyaml);
        Assert.NotNull(result);
        Assert.Equal(Framing.AO40FEC, result!.Framing);
        Assert.True(result.Differential);
        Assert.True(SignalParamsResolver.IsTelemetryDecodable(result));

        // Same spacecraft, different RF transmitter: never mistake the
        // 145.95-145.97 MHz analogue transponder for the FEC beacon.
        var otherDownlink = new SatnogsDbTransmitter
        {
            uuid = "ao73-linear", norad_cat_id = 39444,
            downlink_low = 145960000, baud = 1200,
            description = "1k2 BPSK",
            mode = "BPSK", DownlinkMode = "USB"
        };
        Assert.Equal(Framing.Unknown,
            SignalParamsResolver.Resolve(otherDownlink)!.Framing);
    }

    [Theory]
    [InlineData(Modulation.FSK, TelemetryFskAfInput.ToneAudio, false)]
    [InlineData(Modulation.GFSK, TelemetryFskAfInput.ToneAudio, false)]
    [InlineData(Modulation.GMSK, TelemetryFskAfInput.ToneAudio, false)]
    [InlineData(Modulation.FSK, TelemetryFskAfInput.FmDiscriminator, true)]
    [InlineData(Modulation.GFSK, TelemetryFskAfInput.FmDiscriminator, true)]
    [InlineData(Modulation.GMSK, TelemetryFskAfInput.FmDiscriminator, true)]
    [InlineData(Modulation.AFSK, TelemetryFskAfInput.ToneAudio, true)]
    [InlineData(Modulation.BPSK, TelemetryFskAfInput.FmDiscriminator, false)]
    public void AfSignalInterpretationKeepsOriginalToneAudioByDefault(
        Modulation modulation, TelemetryFskAfInput mode, bool usesDiscriminator)
    {
        Assert.Equal(usesDiscriminator,
            TelemetryAudioReceiveSession.UseDiscriminatorAf(true, modulation, mode));
        // Even for a user-selected FM discriminator, raw 12-kHz IF
        // must never be treated as already-demodulated AF.
        Assert.False(TelemetryAudioReceiveSession.UseDiscriminatorAf(false, modulation, mode));
    }

    [Fact]
    public void FskDiscriminatorDeviationFollowsBaudOrExplicitMetadata()
    {
        var gmsk = new SignalParams(9600, Modulation.GMSK,
            Framing.AX100ASM, 48000);
        Assert.Equal(2400, TelemetryAudioReceiveSession.FskAfPeakDeviation(gmsk, 0));
        Assert.Equal(3200, TelemetryAudioReceiveSession.FskAfPeakDeviation(gmsk, 3200));
        var custom = gmsk with { Deviation = 4100 };
        Assert.Equal(4100, TelemetryAudioReceiveSession.FskAfPeakDeviation(custom, 0));
        Assert.Equal(1800, TelemetryAudioReceiveSession.FskAfPeakDeviation(null, 0));
    }

    private sealed class ReceiverProbe : TelemetryDecocder
    {
        internal ReceiverProbe(SignalParams p) : base(p, 39444, true, false, null) { }

        internal void FeedDirect(DataEventArgs<Complex32> args) => Process(args);
    }

    [Fact]
    public void PooledIqBufferOnlyProcessesItsValidCount()
    {
        var p = new SignalParams(1200, Modulation.BPSK,
            Framing.AO40FEC, 48000) { Differential = true };
        var data = new Complex32[16384];
        // Filled tail represents stale IQ from a previous buffer rental.
        for (int i = 256; i < data.Length; i++)
            data[i] = new Complex32(0.8f, 0.3f);
        using var expected = new StreamingPipeline(p);
        using var actual = new ReceiverProbe(p);
        expected.Push(data.AsSpan(0, 256));
        actual.FeedDirect(new DataEventArgs<Complex32>(data, 256));
        Assert.Equal(expected.SamplesProcessed, actual.Pipeline!.SamplesProcessed);

        // Now send enough samples to cross the processing frame boundary:
        // the observed FFT timeline must still match an exact-count Push.
        var second = new Complex32[8192];
        for (int i = 0; i < second.Length; i++)
            second[i] = new Complex32(0.02f, -0.03f);
        expected.Push(second.AsSpan(0, 4096));
        actual.FeedDirect(new DataEventArgs<Complex32>(second, 4096));
        Assert.Equal(expected.SamplesProcessed, actual.Pipeline.SamplesProcessed);
    }
}
