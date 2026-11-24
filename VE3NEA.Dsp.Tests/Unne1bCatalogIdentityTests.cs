using SkyRoof;
using SkyRoof.Satellites;
using VE3NEA.SkyTlm.Core;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class Unne1bCatalogIdentityTests
{
    private static SatnogsDbSatellite Satellite(
        string satId, int norad, string name) => new()
    {
        sat_id = satId,
        norad_cat_id = norad,
        name = name,
        names = "",
        status = "in orbit",
        telemetries = new SatnogsDbSatellite.Telemetries(),
        associated_satellites = new List<string>()
    };

    private static SatnogsDbTransmitter Transmitter(
        SatnogsDbSatellite sat, string uuid, string mode) => new()
    {
        uuid = uuid,
        sat_id = sat.sat_id,
        norad_cat_id = sat.norad_cat_id,
        Satellite = sat,
        description = mode == "FM"
            ? "FM Voice Repeater"
            : "200 bps FSK telemetry",
        mode = mode,
        DownlinkMode = mode,
        downlink_low = 436888000L,
        uplink_low = mode == "FM" ? 145925000L : null,
        baud = mode == "FSK" ? 200 : null,
        service = "Amateur",
        type = mode == "FM" ? "Transceiver" : "Transmitter",
        alive = true
    };

    [Fact]
    public void MergesTemporaryAndOfficialRecordsWithoutLosingRadioOrOrbit()
    {
        var primary = Satellite("SATNOGS-98205", 98205, "UNNE-1B");
        var fm = Transmitter(primary, "fm-repeater", "FM");
        var fsk = Transmitter(primary, "fsk-beacon", "FSK");
        primary.Transmitters.AddRange(new[] { fm, fsk });
        var second = Satellite("SATNOGS-70002", 70002, "UNNE-1B (HADES-E2)");
        var official = Satellite("CUSTOM-101101", 101101, "UNNE-1B (HADES-E2)");
        var orbit = new SatnogsDbTle
        {
            norad_cat_id = 101101,
            tle0 = "UNNE-1B (HADES-E2)",
            updated = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc),
            tle_source = "CelesTrak OMM CSV"
        };
        official.SetAutomaticTle(orbit, DateTime.UtcNow);

        var catalog = new Dictionary<string, SatnogsDbSatellite>
        {
            [primary.sat_id] = primary,
            [second.sat_id] = second,
            [official.sat_id] = official
        };
        Assert.True(SatnogsDb.NormalizeUnne1bRecords(catalog, DateTime.UtcNow));
        Assert.Single(catalog);
        Assert.Same(primary, Assert.Single(catalog.Values));
        Assert.Equal(101101, primary.norad_cat_id);
        Assert.Same(orbit, primary.Tle);
        Assert.Equal(2, primary.Transmitters.Count);
        Assert.All(primary.Transmitters, tx =>
        {
            Assert.Same(primary, tx.Satellite);
            Assert.Equal(primary.sat_id, tx.sat_id);
            Assert.Equal(101101, tx.norad_cat_id);
        });
        Assert.Same(primary, SatnogsDb.ResolveSatellite(catalog, second.sat_id));
        Assert.Same(primary, SatnogsDb.ResolveSatellite(catalog, official.sat_id));
        Assert.Contains("CUSTOM-101101", primary.MergedSatIds);
        Assert.Contains("101101", primary.SearchText);
        Assert.Contains("98205", primary.SearchText);
        Assert.Contains("70002", primary.SearchText);
        Assert.Contains("HADES-E2", primary.AllNames);
        Assert.False(SatnogsDb.NormalizeUnne1bRecords(catalog, DateTime.UtcNow));

        Assert.True(TelemetryPanel.ShouldUseUnneTelemetrySibling(primary, fm));
        var sibling = TelemetryPanel.ResolveUnne1bFmTelemetrySource(primary, fm);
        Assert.NotNull(sibling);
        Assert.Equal(Framing.HADES, sibling!.Value.Params.Framing);
        Assert.Equal(200, sibling.Value.Params.Baud);
        Assert.Equal(101101, SignalParamsResolver.TelemetryDecoderNoradId(
            primary, fsk, SignalParamsResolver.Resolve(fsk)!));
    }

    [Fact]
    public void LegacyFmOnlyRecordStillSelectsNativeUnneTelemetry()
    {
        var satellite = Satellite("LEGACY-HADES", 70002, "UNNE-1B(HADES-E2)");
        var fm = Transmitter(satellite, "fm-only", "FM");
        satellite.Transmitters.Add(fm);
        var catalog = new Dictionary<string, SatnogsDbSatellite>
        {
            [satellite.sat_id] = satellite
        };
        Assert.True(SatnogsDb.NormalizeUnne1bRecords(catalog, DateTime.UtcNow));
        Assert.Equal(101101, satellite.norad_cat_id);
        var sibling = TelemetryPanel.ResolveUnne1bFmTelemetrySource(satellite, fm);
        Assert.NotNull(sibling);
        Assert.Same(fm, sibling!.Value.Transmitter);
        Assert.Equal(Framing.HADES, sibling.Value.Params.Framing);
        Assert.Equal(101101, SignalParamsResolver.TelemetryDecoderNoradId(
            satellite, fm, sibling.Value.Params));
    }

    [Fact]
    public void Unrelated70002IsNeverRewrittenOrMisidentified()
    {
        var unrelated = Satellite("OTHER", 70002, "Other Payload");
        var otherFm = Transmitter(unrelated, "other", "FM");
        unrelated.Transmitters.Add(otherFm);
        var catalog = new Dictionary<string, SatnogsDbSatellite>
        {
            [unrelated.sat_id] = unrelated
        };
        Assert.False(SatnogsDb.NormalizeUnne1bRecords(catalog, DateTime.UtcNow));
        Assert.Equal(70002, unrelated.norad_cat_id);
        Assert.False(SignalParamsResolver.IsUnne1bDownlink(otherFm, unrelated));
    }

    [Fact]
    public void BothAliasNamesTriggerTelemetryRecognitionBeforeCatalogMigration()
    {
        foreach (var name in new[] { "UNNE-1B (HADES-E2)", "HADES-E2", "SO-131" })
        {
            var legacy = Satellite("ALIAS", 70002, name);
            var fm = Transmitter(legacy, "fm", "FM");
            Assert.True(SignalParamsResolver.IsUnne1bDownlink(fm, legacy));
            Assert.True(TelemetryPanel.ShouldUseUnneTelemetrySibling(legacy, fm));
        }
    }
}
