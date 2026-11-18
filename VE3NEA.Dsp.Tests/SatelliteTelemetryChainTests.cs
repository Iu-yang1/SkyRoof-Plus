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
    [InlineData("CCSDS Reed-Solomon", "differential", true)]
    [InlineData("CCSDS Reed-Solomon", "none", false)]
    [InlineData("AX.25 G3RUH", null, null)]
    [InlineData("AX.25 G3RUH", "none", false)]
    public void UnspecifiedPskPrecodingHonorsEachProtocolAndPreservesAutodetectWhereSupported(
        string framing, string? precoding, bool? expected)
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
        // For AX.25, null allows the modem's CRC-gated coherent/DBPSK
        // trial; for CCSDS, this flag controls post-demodulation NRZ-I
        // precoding and defaults to false, not a physical DBPSK trial.
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

    [Fact]
    public void Unne1bDatabaseBpsk200RowAutomaticallyResolvesToFskHades()
    {
        var sat = new SatnogsDbSatellite
        {
            sat_id = "UNNE-1B", norad_cat_id = Unne1bDeframer.NoradId,
            name = "UNNE-1B"
        };
        var tx = new SatnogsDbTransmitter
        {
            uuid = "unne-bpsk-wrong", Satellite = sat,
            norad_cat_id = Unne1bDeframer.NoradId,
            description = "Mode U - 200bps BPSK telemetry",
            mode = "BPSK", DownlinkMode = "BPSK",
            downlink_low = 436888000,
            baud = 200,
            gr_sats = new GrSatsInfo { baudrate = 200, modulation = "BPSK", deviation = 500 }
        };
        Assert.True(SignalParamsResolver.IsUnne1b200BaudTelemetry(tx));
        var resolved = SignalParamsResolver.Resolve(tx)!;
        Assert.Equal(Modulation.FSK, resolved.Modulation);
        Assert.Equal(Framing.HADES, resolved.Framing);
        Assert.Equal(200, resolved.Baud);
        Assert.Equal(820, resolved.Deviation); // verified genuine IQ/CRC baseline
        Assert.True(SignalParamsResolver.IsTelemetryDecodable(resolved));

        tx.manual = new GrSatsInfo
        {
            modulation = "FSK", framing = "HADES", baudrate = 800,
            deviation = 820
        };
        resolved = SignalParamsResolver.Resolve(tx)!;
        Assert.Equal(800, resolved.Baud); // operator's explicit override wins
        Assert.Equal(820, resolved.Deviation);
    }

    [Theory]
    [InlineData("Mode V/U - FM Repeater (Voice + (A)FSK)", "FM", null)]
    [InlineData("Mode U - CW", "CW", null)]
    [InlineData("Mode U - 200bps BPSK", "BPSK", 200)]
    public void Unne1bTelemetryCorrectionNeverConvertsFmOrCw(
        string description, string mode, int? baud)
    {
        var tx = new SatnogsDbTransmitter
        {
            norad_cat_id = Unne1bDeframer.NoradId,
            description = description, mode = mode, DownlinkMode = mode,
            downlink_low = 436888000, baud = baud
        };
        var resolved = SignalParamsResolver.Resolve(tx)!;
        if (baud == 200)
        {
            Assert.Equal(Modulation.FSK, resolved.Modulation);
            Assert.Equal(Framing.HADES, resolved.Framing);
        }
        else
        {
            Assert.False(SignalParamsResolver.IsUnne1b200BaudTelemetry(tx));
            Assert.Equal(mode == "FM" ? Modulation.FM : Modulation.CW,
                resolved.Modulation);
        }
    }

    [Fact]
    public void UnneFmRepeaterViewUsesSameFrequencyFskSiblingWithoutRetuning()
    {
        var sat = new SatnogsDbSatellite
        {
            sat_id = "UNNE-1B", norad_cat_id = Unne1bDeframer.NoradId,
            name = "UNNE-1B"
        };
        var fm = new SatnogsDbTransmitter
        {
            uuid = "unne-fm", Satellite = sat,
            norad_cat_id = Unne1bDeframer.NoradId,
            description = "Mode V/U - FM Repeater (Voice + (A)FSK)", mode = "FM", DownlinkMode = "FM",
            downlink_low = 436888000
        };
        var telemetry = new SatnogsDbTransmitter
        {
            uuid = "unne-tlm", Satellite = sat,
            norad_cat_id = Unne1bDeframer.NoradId,
            description = "Mode U - 200bps BPSK", mode = "BPSK",
            DownlinkMode = "BPSK", downlink_low = 436888000, baud = 200
        };
        sat.Transmitters.AddRange([fm, telemetry]);
        Assert.True(SignalParamsResolver.IsUnne1bFmRepeater(fm));
        Assert.Equal(Modulation.FM, SignalParamsResolver.Resolve(fm)!.Modulation);
        Assert.True(TelemetryPanel.ShouldUseUnneTelemetrySibling(sat, fm,
            SignalParamsResolver.Resolve(fm)));
        Assert.Same(telemetry, CoChannel.RankedTelemetrySibling(sat, fm));
        Assert.False(TelemetryPanel.ShouldUseUnneTelemetrySibling(sat, telemetry,
            SignalParamsResolver.Resolve(telemetry)));
        var other = new SatnogsDbSatellite { norad_cat_id = 99999 };
        var otherFm = new SatnogsDbTransmitter
        {
            norad_cat_id = 99999, Satellite = other,
            mode = "FM", DownlinkMode = "FM", description = "FM repeater",
            downlink_low = 436888000
        };
        Assert.False(TelemetryPanel.ShouldUseUnneTelemetrySibling(other, otherFm,
            SignalParamsResolver.Resolve(otherFm)));
    }

    [Fact]
    public async Task DecodedBinAndLogPreserveBytesIntegrityAndCfo()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "skyroof-telemetry-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archive = new TelemetryFrameArchive(root);
            byte[] bytes = [0x3C, 0x01, 0x02, 0x03];
            archive.Offer(new Frame
            {
                Bytes = bytes, Framing = Framing.HADES,
                CrcValid = false, CfoHz = -1211.2, SnrDb = 28.6,
                CorrectedBits = 2, Note = "UNNE-1B candidate"
            }, Unne1bDeframer.NoradId, "UNNE-1B", "FSK 200", false);
            await archive.CompleteAsync();
            Assert.Equal(1, archive.Saved);
            Assert.Equal(0, archive.Errors);
            string bin = Assert.Single(Directory.GetFiles(root, "*.bin",
                SearchOption.AllDirectories));
            string log = Assert.Single(Directory.GetFiles(root, "*.log",
                SearchOption.AllDirectories));
            Assert.Equal(bytes, File.ReadAllBytes(bin));
            string metadata = File.ReadAllText(log);
            Assert.Contains("CRC/FEC: FAIL", metadata);
            Assert.Contains("CFO: -1211.2 Hz", metadata);
            Assert.Contains("HEX: 3C010203", metadata);
            Assert.Contains("Corrections: 2", metadata);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
