using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal static IReadOnlyList<QsoRecord> ParseAdif(string text, string fallbackStationCall)
    {
        var result = new List<QsoRecord>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int offset = 0;
        while (offset < text.Length)
        {
            int start = text.IndexOf('<', offset);
            if (start < 0) break;
            int end = text.IndexOf('>', start + 1);
            if (end < 0) throw new InvalidDataException("Unterminated ADIF tag.");
            string tag = text[(start + 1)..end];
            offset = end + 1;
            if (tag.Equals("EOH", StringComparison.OrdinalIgnoreCase)) fields.Clear();
            else if (tag.Equals("EOR", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(MapAdif(fields, fallbackStationCall));
                fields.Clear();
            }
            else ReadAdifField(text, tag, fields, ref offset);
        }
        if (fields.Count > 0) throw new InvalidDataException("ADIF record missing EOR.");
        return result;
    }

    private static void ReadAdifField(string text, string tag, Dictionary<string, string> fields, ref int offset)
    {
        string[] parts = tag.Split(':');
        if (parts.Length is < 2 or > 3 || parts[0].Length == 0 ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int length) ||
            length > text.Length - offset)
            throw new InvalidDataException("Invalid ADIF field length: " + tag);
        fields[parts[0]] = text.Substring(offset, length);
        offset += length;
    }

    private static QsoRecord MapAdif(Dictionary<string, string> fields, string fallbackStationCall)
    {
        string Field(string key) => fields.TryGetValue(key, out var value) ? value : "";
        string Upper(string key) => Field(key).Trim().ToUpperInvariant();
        var qso = new QsoRecord
        {
            Call = Upper("CALL"), StationCall = Upper("STATION_CALLSIGN"), MyGrid = Upper("MY_GRIDSQUARE"),
            Grid = Upper("GRIDSQUARE"), Band = Field("BAND").Trim().ToLowerInvariant(),
            Mode = Upper("MODE"), Submode = Upper("SUBMODE"), Satellite = Upper("SAT_NAME"),
            Propagation = Upper("PROP_MODE"), Sent = Field("RST_SENT"), Received = Field("RST_RCVD"),
            Name = Field("NAME"), State = Field("STATE"), Notes = Field("COMMENT"),
            TxHz = AdifHz(Field("FREQ")), RxHz = AdifHz(Field("FREQ_RX")),
            UtcMilliseconds = AdifUtc(Field("QSO_DATE"), Field("TIME_ON"), Field("APP_SKYROOF_UTC_MS"))
        };
        if (qso.StationCall.Length == 0) qso.StationCall = fallbackStationCall.Trim().ToUpperInvariant();
        if (qso.Mode is "FT4" or "Q65")
        {
            qso.Submode = qso.Mode;
            qso.Mode = "MFSK";
        }
        ValidateImportedQso(qso);
        string id = Field("APP_SKYROOF_QSO_ID");
        qso.Id = Guid.TryParseExact(id, "N", out var existing) ? existing.ToString("N") :
            Convert.ToHexString(SHA256.HashData(Google.Protobuf.MessageExtensions.ToByteArray(qso)).AsSpan(0, 16)).ToLowerInvariant();
        return qso;
    }

    private static void ValidateImportedQso(QsoRecord qso)
    {
        static bool CallValid(string call) => call.Length is >= 3 and <= 32 &&
            call.Any(char.IsAsciiDigit) && call.All(c => char.IsAsciiLetterOrDigit(c) || c == '/');
        if (!CallValid(qso.Call) || !CallValid(qso.StationCall) || qso.Band.Length == 0 ||
            qso.Mode.Length == 0 || (qso.Propagation == "SAT" && qso.Satellite.Length == 0))
            throw new InvalidDataException("ADIF requires CALL, station callsign (or MY CALL), BAND, MODE and SAT_NAME for SAT.");
        if (qso.UtcMilliseconds <= 0 || qso.CalculateSize() > 32700)
            throw new InvalidDataException("ADIF record timestamp or size is outside the Station logbook range.");
    }

    private static long AdifUtc(string date, string time, string exact)
    {
        if (!DateTimeOffset.TryParseExact(date + time, new[] { "yyyyMMddHHmm", "yyyyMMddHHmmss" },
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            throw new InvalidDataException("Invalid ADIF QSO_DATE/TIME_ON.");
        long milliseconds = utc.ToUnixTimeMilliseconds();
        if (long.TryParse(exact, NumberStyles.None, CultureInfo.InvariantCulture, out long precise) &&
            precise >= milliseconds && precise - milliseconds < 1000)
            return precise;
        return milliseconds;
    }

    private static long AdifHz(string value)
    {
        if (value.Length == 0) return 0;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal mhz) ||
            mhz < 0 || mhz > long.MaxValue / 1_000_000m)
            throw new InvalidDataException("Invalid ADIF frequency: " + value);
        return (long)decimal.Round(mhz * 1_000_000m, 0, MidpointRounding.AwayFromZero);
    }

    internal async Task<int> ImportAdifAsync(IReadOnlyList<QsoRecord> records, Action<int> progress, CancellationToken token)
    {
        int count = 0;
        foreach (var qso in records)
        {
            await SaveQsoAsync(qso, token);
            progress(++count);
        }
        return count;
    }
}
