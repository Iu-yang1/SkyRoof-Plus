namespace SkyRoof.Remote.Ipc;

public static class AdifExtensionFields
{
    private static readonly HashSet<string> Canonical = new(StringComparer.OrdinalIgnoreCase)
    {
        "CALL", "STATION_CALLSIGN", "MY_GRIDSQUARE", "GRIDSQUARE", "BAND", "MODE", "SUBMODE",
        "SAT_NAME", "PROP_MODE", "RST_SENT", "RST_RCVD", "NAME", "STATE", "COMMENT", "FREQ",
        "FREQ_RX", "QSO_DATE", "TIME_ON", "APP_SKYROOF_QSO_ID", "APP_SKYROOF_UTC_MS", "EOH", "EOR"
    };

    public static bool IsCanonical(string name) => Canonical.Contains(name);

    public static void Normalize(QsoRecord record)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var types = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in record.ExtraAdifFields)
        {
            string key = item.Key.ToUpperInvariant();
            if (IsCanonical(key) || key.Length is < 1 or > 128 ||
                !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || !fields.TryAdd(key, item.Value))
                throw new InvalidDataException("Invalid or duplicate ADIF extension field: " + key);
        }
        foreach (var item in record.ExtraAdifTypes)
        {
            string key = item.Key.ToUpperInvariant(), type = item.Value.ToUpperInvariant();
            if (!fields.ContainsKey(key) || type.Length != 1 || !char.IsAsciiLetter(type[0]) || !types.TryAdd(key, type))
                throw new InvalidDataException("Invalid ADIF extension type: " + key);
        }
        record.ExtraAdifFields.Clear();
        record.ExtraAdifTypes.Clear();
        foreach (var item in fields) record.ExtraAdifFields.Add(item.Key, item.Value);
        foreach (var item in types) record.ExtraAdifTypes.Add(item.Key, item.Value);
    }

    public static string Format(QsoRecord record)
    {
        var result = new System.Text.StringBuilder();
        foreach (var item in record.ExtraAdifFields.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            result.Append('<').Append(item.Key).Append(':').Append(item.Value.Length);
            if (record.ExtraAdifTypes.TryGetValue(item.Key, out var type)) result.Append(':').Append(type);
            result.Append('>').Append(item.Value);
        }
        return result.ToString();
    }
}
