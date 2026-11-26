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
            Id = Guid.NewGuid().ToString("N"), UtcMilliseconds = 1795708800000,
            StationCall = "K1ABC", Call = "W1AW", Grid = "FN31", Band = "70cm", Mode = "MFSK",
            Submode = "FT4", Satellite = "RS-44", Propagation = "SAT", Sent = "-14", Received = "-16",
            TxHz = 435650000, RxHz = 145900000
        };
        var first = await client.SaveQsoAsync(qso, token);
        var retry = await client.SaveQsoAsync(qso, token);
        var query = new QsoSearch { Call = qso.Call, Limit = 10 };
        var history = await client.SearchQsoAsync(query, token);
        var export = await client.ExportQsoAsync(query, token);
        if (!first.Equals(retry) || history.Records.Count != 1 || export.Count != 1 ||
            !export.Content.Contains("<SUBMODE:3>FT4") || !export.Content.Contains("<PROP_MODE:3>SAT") ||
            !export.Content.Contains("<FREQ:6>435.65") || !export.Content.Contains("<FREQ_RX:5>145.9"))
            throw new InvalidDataException("Station QSO idempotency/ADIF export mismatch.");
        var changed = qso.Clone(); changed.Notes = "different";
        try { await client.SaveQsoAsync(changed, token); throw new InvalidDataException("Changed idempotency key accepted."); }
        catch (RpcException error) when (error.StatusCode == StatusCode.AlreadyExists)
        { Console.WriteLine("PASS: Station durable QSO, idempotent retry, conflicting ID rejection and ADIF fields."); }
    }
}
