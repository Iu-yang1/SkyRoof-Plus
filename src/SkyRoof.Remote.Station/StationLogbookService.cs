using System.Globalization;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class StationLogbookService : StationLogbook.StationLogbookBase
{
    private readonly object gate = new();
    private readonly string directory = Environment.GetEnvironmentVariable("SKYROOF_LOGBOOK_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyRoofRemote", "logbook");

    public override Task<QsoRecord> Save(QsoRecord request, ServerCallContext context)
    {
        var qso = Normalize(request);
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, qso.Id + ".json");
            if (File.Exists(path))
            {
                var prior = JsonParser.Default.Parse<QsoRecord>(File.ReadAllText(path));
                if (!prior.Equals(qso))
                    throw new RpcException(new Status(StatusCode.AlreadyExists, "QSO ID already contains different data."));
                return Task.FromResult(prior);
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(qso)));
                    file.Flush(true);
                }
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return Task.FromResult(qso);
    }

    public override Task<QsoSearchResult> Search(QsoSearch request, ServerCallContext context)
    {
        lock (gate)
        {
            var list = ReadPage(request, context.CancellationToken);
            var result = new QsoSearchResult();
            result.Records.AddRange(list.Take(PageSize(request)));
            if (list.Count > result.Records.Count)
                result.NextBeforeId = result.Records[^1].Id;
            return Task.FromResult(result);
        }
    }

    public override Task<QsoAdif> Export(QsoSearch request, ServerCallContext context)
    {
        lock (gate)
        {
            var page = ReadPage(request, context.CancellationToken);
            var records = page.Take(PageSize(request)).ToArray();
            var content = new StringBuilder("SkyRoof Remote ADIF export\n<PROGRAMID:14>SkyRoof Remote<EOH>\n");
            foreach (var record in records) content.Append(FormatAdif(record));
            return Task.FromResult(new QsoAdif { Content = content.ToString(), Count = records.Length,
                NextBeforeId = page.Count > records.Length ? records[^1].Id : "" });
        }
    }

    private static int PageSize(QsoSearch query) => Math.Clamp(query.Limit == 0 ? 100 : query.Limit, 1, 1000);

    private List<QsoRecord> ReadPage(QsoSearch query, CancellationToken ct)
    {
        var records = new List<QsoRecord>();
        if (!Directory.Exists(directory)) return records;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var record = JsonParser.Default.Parse<QsoRecord>(File.ReadAllText(path));
            if ((query.FromUtcMilliseconds == 0 || record.UtcMilliseconds >= query.FromUtcMilliseconds) &&
                (query.ToUtcMilliseconds == 0 || record.UtcMilliseconds <= query.ToUtcMilliseconds) &&
                (string.IsNullOrWhiteSpace(query.Call) || record.Call.Contains(query.Call.Trim(), StringComparison.OrdinalIgnoreCase)))
                records.Add(record);
        }
        var sorted = records.OrderByDescending(x => x.UtcMilliseconds).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        int start = query.BeforeId.Length == 0 ? 0 : sorted.FindIndex(x => x.Id == query.BeforeId) + 1;
        if (query.BeforeId.Length > 0 && start == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Unknown logbook cursor."));
        return sorted.Skip(start).Take(PageSize(query) + 1).ToList();
    }

    private static QsoRecord Normalize(QsoRecord request)
    {
        var qso = request.Clone();
        if (!Guid.TryParseExact(qso.Id, "N", out _))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "QSO requires a client-generated GUID in N format."));
        qso.Id = qso.Id.ToLowerInvariant();
        qso.Call = qso.Call.Trim().ToUpperInvariant();
        qso.StationCall = qso.StationCall.Trim().ToUpperInvariant();
        qso.Mode = qso.Mode.Trim().ToUpperInvariant();
        qso.Submode = qso.Submode.Trim().ToUpperInvariant();
        qso.Grid = qso.Grid.Trim().ToUpperInvariant();
        qso.MyGrid = qso.MyGrid.Trim().ToUpperInvariant();
        qso.Band = qso.Band.Trim().ToLowerInvariant();
        qso.Propagation = qso.Propagation.Trim().ToUpperInvariant();
        qso.Satellite = qso.Satellite.Trim().ToUpperInvariant();
        if (!ValidCall(qso.Call) || !ValidCall(qso.StationCall) || qso.Mode.Length == 0 ||
            qso.Band.Length == 0 || qso.TxHz < 0 || qso.RxHz < 0 ||
            qso.UtcMilliseconds <= 0 || qso.UtcMilliseconds > 253402300799999 ||
            (qso.Propagation == "SAT" && qso.Satellite.Length == 0))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Call, station call, UTC, band and mode required; SAT requires satellite."));
        try { AdifExtensionFields.Normalize(qso); }
        catch (InvalidDataException error) { throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message)); }
        if (qso.ToByteArray().Length > 32768)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "QSO record exceeds 32 KiB."));
        return qso;
    }

    private static bool ValidCall(string call) => call.Length is >= 3 and <= 32 &&
        call.Any(char.IsAsciiDigit) && call.All(c => char.IsAsciiLetterOrDigit(c) || c == '/');
}
