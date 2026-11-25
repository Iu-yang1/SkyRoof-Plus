using System.Globalization;
using System.Text.Json;

namespace SkyRoof.Remote.Station;

internal static partial class CatalogTelemetryResolver
{
    private static string? Field(CatalogCacheTransmitter tx, string name) =>
        Property(tx.Manual, name) ?? Property(tx.GrSats, name);

    private static string? Property(JsonElement? layer, string name) =>
        layer is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var field)
            ? Text(field) : null;

    private static string? Text(JsonElement? value) => value is not { } field ? null : field.ValueKind switch
    {
        JsonValueKind.String => field.GetString(),
        JsonValueKind.Number => field.GetRawText(),
        _ => null
    };

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : null;

    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string Modulation(string text)
    {
        foreach (string mode in new[] { "GMSK", "GFSK", "AFSK", "FSK", "QPSK", "BPSK" })
            if (Contains(text, mode)) return mode;
        return "";
    }

    private static string Framing(string text)
    {
        if (Contains(text, "AO-40 FEC short") || Contains(text, "AO-40 FEC CRC-16-ARC") ||
            Contains(text, "AO-40 uncoded") || Contains(text, "ESEO") || Contains(text, "YUSAT")) return "";
        if (Contains(text, "AX100") || Contains(text, "AX.100"))
            return Contains(text, "Reed") || Contains(text, "Mode 6") || Contains(text, "AX100RS") ? "AX100RS" : "AX100ASM";
        foreach (string name in new[] { "CCSDS", "USP", "GEOSCAN" })
            if (Contains(text, name)) return name;
        if (Contains(text, "AO40FEC") || Contains(text, "AO-40 FEC")) return "AO40FEC";
        if (Contains(text, "AX25") || Contains(text, "AX.25") || Contains(text, "G3RUH")) return "AX25G3RUH";
        if (Contains(text, "HADES") || Contains(text, "GENESIS")) return "HADES";
        return "";
    }
}
