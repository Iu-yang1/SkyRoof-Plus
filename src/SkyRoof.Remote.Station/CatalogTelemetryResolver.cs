using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

internal static partial class CatalogTelemetryResolver
{
    internal static CatalogTelemetryProfile? Resolve(CatalogCacheSatellite satellite, CatalogCacheTransmitter tx)
    {
        if (tx.DownlinkLow is not > 0) return null;
        string rawMode = tx.Mode ?? tx.DownlinkMode ?? "";
        string modulation = Modulation(Field(tx, "modulation") ?? rawMode);
        string framingText = Field(tx, "framing") ?? "";
        string framing = Framing(framingText.Length > 0 ? framingText : rawMode + " " + tx.Description);
        double baud = Number(Field(tx, "baudrate")) ?? Number(Text(tx.Baud)) ?? 0;
        double deviation = Number(Field(tx, "deviation")) ?? 0;
        bool unne = IsUnne(satellite, tx);
        string source = tx.Manual is { ValueKind: System.Text.Json.JsonValueKind.Object } ? "manual / catalog" :
            tx.GrSats is { ValueKind: System.Text.Json.JsonValueKind.Object } ? "SatYAML / catalog" : "SatNOGS";
        if (unne && (modulation is "BPSK" or "FSK") && Field(tx, "framing") is null)
        {
            modulation = "FSK";
            framing = "HADES";
            if (Field(tx, "baudrate") is null) baud = 200;
            if (Field(tx, "deviation") is null) deviation = 820;
            source = "UNNE-1B verified protocol / " + source;
        }
        if (satellite.Norad == 39444 && Math.Abs(tx.DownlinkLow.Value - 145935000) <= 5000 &&
            modulation == "BPSK" && Field(tx, "framing") is null && framing.Length == 0)
        {
            framing = "AO40FEC";
            if (baud <= 0) baud = 1200;
            source = "AO-73 beacon / " + source;
        }
        if (modulation.Length == 0 || framing.Length == 0 || baud <= 0 || baud >= 24000) return null;
        string precoding = Field(tx, "precoding") ?? "";
        var result = new CatalogTelemetryProfile
        {
            Source = source, Modulation = modulation, Framing = framing, Baud = baud,
            DeviationHz = modulation == "AFSK" ? 500 : deviation,
            AfCarrierHz = modulation == "AFSK" ? 1700 : 0,
            DifferentialMode = precoding.Length > 0 ? Contains(precoding, "differential") ? "differential" : "coherent" :
                framing == "AO40FEC" ? "differential" : framing == "CCSDS" ? "coherent" : "auto",
            Manchester = Contains(Field(tx, "modulation") ?? rawMode, "Manchester"),
            DecoderProtocol = unne && framing == "HADES" ? "unne1b" : ""
        };
        if (framing == "CCSDS") ApplyCcsds(tx, framingText, result);
        return result;
    }

    private static bool IsUnne(CatalogCacheSatellite satellite, CatalogCacheTransmitter tx) =>
        tx.DownlinkLow == 436888000 &&
        (satellite.Norad == 101101 || satellite.Norad == 98205 ||
            Contains(satellite.Name, "UNNE-1B") || Contains(satellite.Name, "HADES-E2"));

    private static void ApplyCcsds(CatalogCacheTransmitter tx, string framing, CatalogTelemetryProfile profile)
    {
        profile.RsBasis = Field(tx, "rs_basis") ?? "";
        profile.FrameSize = (int)(Number(Field(tx, "frame_size")) ?? 0);
        profile.RsInterleaving = (int)(Number(Field(tx, "rs_interleaving")) ?? 0);
        profile.Convolutional = Contains(framing, "concatenated") ? Field(tx, "convolutional") ?? "CCSDS" : "";
        profile.RsMode = Contains(framing, "uncoded") ? "off" : "on";
        string? scrambler = Field(tx, "scrambler");
        profile.ScramblerMode = scrambler is null ? "" : Contains(scrambler, "none") ? "off" : "on";
    }
}
