using System.Globalization;
using VE3NEA;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class StationLogbookService
{
    private static string FormatAdif(QsoRecord qso)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(qso.UtcMilliseconds);
        var entry = new AdifEntry
        {
            ["QSO_DATE"] = utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["TIME_ON"] = utc.ToString("HHmmss", CultureInfo.InvariantCulture),
            ["CALL"] = qso.Call, ["STATION_CALLSIGN"] = qso.StationCall,
            ["BAND"] = qso.Band, ["MODE"] = qso.Mode
        };
        entry["APP_SKYROOF_QSO_ID"] = qso.Id;
        entry["APP_SKYROOF_UTC_MS"] = qso.UtcMilliseconds.ToString(CultureInfo.InvariantCulture);
        Add(entry, "SUBMODE", qso.Submode);
        Add(entry, "MY_GRIDSQUARE", qso.MyGrid);
        Add(entry, "GRIDSQUARE", qso.Grid);
        Add(entry, "PROP_MODE", qso.Propagation);
        Add(entry, "SAT_NAME", qso.Satellite);
        Add(entry, "RST_SENT", qso.Sent);
        Add(entry, "RST_RCVD", qso.Received);
        Add(entry, "NAME", qso.Name);
        Add(entry, "STATE", qso.State);
        Add(entry, "COMMENT", qso.Notes);
        if (qso.TxHz > 0) entry["FREQ"] = (qso.TxHz / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);
        if (qso.RxHz > 0) entry["FREQ_RX"] = (qso.RxHz / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);
        return AdifFile.QsoToString(entry);
    }

    private static void Add(AdifEntry entry, string key, string value)
    {
        if (value.Length > 0) entry[key] = value;
    }
}
