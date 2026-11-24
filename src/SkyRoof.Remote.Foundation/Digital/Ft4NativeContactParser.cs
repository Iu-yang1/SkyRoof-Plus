using System.Globalization;

namespace SkyRoof.Remote.Foundation.Digital;

public static class Ft4NativeContactParser
{
    // Match the fixed fields used by the original desktop DecodedItem.
    public static Ft4DecodedContact? Parse(string raw)
    {
        if (raw.Length < 25 || !int.TryParse(raw.AsSpan(7, 3), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int snr)) return null;
        if (raw.Length > 42 && raw[42] == '?') return null;
        string body = raw.Substring(24, Math.Min(35, raw.Length - 24)).Trim();
        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] == "CQ" || body.Contains('?')) return null;
        return new(parts[0].Trim('<', '>'), parts[1].Trim('<', '>'), parts[2], snr);
    }
}
