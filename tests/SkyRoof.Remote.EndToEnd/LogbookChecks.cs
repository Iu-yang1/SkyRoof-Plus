using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;

internal static class LogbookChecks
{
    internal static async Task RunAsync(string pipe)
    {
        using var client = new StationIpcClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var qso = new QsoRecord
        {
            Id = Guid.NewGuid().ToString("N"), UtcMilliseconds = 1795708800123,
            StationCall = "K1ABC", Call = "W1AW", Grid = "FN31", Band = "70cm", Mode = "MFSK",
            Submode = "FT4", Satellite = "RS-44", Propagation = "SAT", Sent = "-14", Received = "-16",
            TxHz = 435650000, RxHz = 145900000, Notes = "literal <EOR> tag"
        };
        qso.ExtraAdifFields.Add("APP_TEST_NOTE", "<EOR>");
        qso.ExtraAdifTypes.Add("APP_TEST_NOTE", "S");
        qso.ExtraAdifFields.Add("TX_PWR", "50");
        qso.ExtraAdifTypes.Add("TX_PWR", "N");
        var first = await client.SaveQsoAsync(qso, token);
        var retry = await client.SaveQsoAsync(qso, token);
        var query = new QsoSearch { Call = qso.Call, Limit = 10 };
        var history = await client.SearchQsoAsync(query, token);
        var export = await client.ExportQsoAsync(query, token);
        if (!first.Equals(retry) || history.Records.Count != 1 || export.Count != 1 ||
            !export.Content.Contains("<SUBMODE:3>FT4") || !export.Content.Contains("<PROP_MODE:3>SAT") ||
            !export.Content.Contains("<FREQ:6>435.65") || !export.Content.Contains("<FREQ_RX:5>145.9") ||
            !export.Content.Contains("<APP_TEST_NOTE:5:S><EOR>") || !export.Content.Contains("<TX_PWR:2:N>50"))
            throw new InvalidDataException("Station QSO idempotency/ADIF export mismatch.");
        await CheckImportAsync(client, first, export, token);
        var changed = qso.Clone(); changed.Notes = "different";
        try { await client.SaveQsoAsync(changed, token); throw new InvalidDataException("Changed idempotency key accepted."); }
        catch (RpcException error) when (error.StatusCode == StatusCode.AlreadyExists)
        { Console.WriteLine("PASS: Station durable QSO, idempotent retry, conflicting ID rejection and ADIF fields."); }
    }
    private static async Task CheckImportAsync(StationIpcClient client, QsoRecord original, QsoAdif export, CancellationToken token)
    {
        var roundTrip = StationIpcClient.ParseAdif(export.Content, "");
        if (roundTrip.Count != 1 || !roundTrip[0].Equals(original))
            throw new InvalidDataException("ADIF round trip changed QSO identity, UTC or fields.");
        await client.ImportAdifAsync(roundTrip, _ => { }, token);
        const string legacy = "<CALL:4>W1AD<QSO_DATE:8>20261126<TIME_ON:4>1234<BAND:3>23m<MODE:3>Q65<COMMENT:5><EOR><EOR>";
        var imported = StationIpcClient.ParseAdif(legacy, "K1ABC");
        if (imported.Count != 1 || imported[0].Submode != "Q65" || imported[0].Mode != "MFSK" ||
            imported[0].Notes != "<EOR>" || imported[0].StationCall != "K1ABC")
            throw new InvalidDataException("Legacy ADIF field import mismatch.");
        var repeat = StationIpcClient.ParseAdif(legacy, "K1ABC");
        if (repeat[0].Id != imported[0].Id)
            throw new InvalidDataException("Legacy ADIF import ID is not deterministic.");
        await client.ImportAdifAsync(imported, _ => { }, token);
        await client.ImportAdifAsync(repeat, _ => { }, token);
        var history = await client.SearchQsoAsync(new QsoSearch { Call = "W1AD" }, token);
        if (history.Records.Count != 1 || !history.Records[0].Equals(imported[0]))
            throw new InvalidDataException("Repeated legacy ADIF import duplicated or changed records.");
        try
        {
            StationIpcClient.ParseAdif("<CALL:99>W1AD<EOR>", "K1ABC");
            throw new InvalidDataException("Truncated ADIF field was accepted.");
        }
        catch (InvalidDataException error) when (error.Message.StartsWith("Invalid ADIF field length", StringComparison.Ordinal))
        {
            Console.WriteLine("PASS: ADIF exact round trip, legacy import, tag-in-value, stable retry and malformed length rejection.");
        }
    }
}
