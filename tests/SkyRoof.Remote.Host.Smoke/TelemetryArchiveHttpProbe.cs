using System.Diagnostics;
using System.Net;
using System.Text.Json;
using SkyRoof.Remote.Foundation.Signals;

internal sealed class TelemetryArchiveHttpProbe : IDisposable
{
    private const string Day = "20261125";
    private const string Older = Day + "_010000_000_00000001_abcdef12";
    private const string Newer = Day + "_010001_000_00000002_abcdef12";
    private const string Incomplete = Day + "_010002_000_00000003_abcdef12";
    private const string Route = "/api/v1/dsp/telemetry/archive";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "skyroof-archive-" + Guid.NewGuid().ToString("N"));
    private static readonly Guid Product = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
    private static readonly byte[] Payload = [0xc0, 0xdb, 1, 2];

    internal void Configure(ProcessStartInfo info)
    {
        string folder = Path.Combine(_root, "39444", Day);
        Directory.CreateDirectory(folder);
        foreach (string stem in new[] { Older, Newer, Incomplete })
        {
            File.WriteAllBytes(Path.Combine(folder, stem + ".bin"), Payload);
            File.WriteAllText(Path.Combine(folder, stem + (stem == Incomplete ? ".log.part" : ".log")), "CRC/FEC: OK");
        }
        string media = Path.Combine(_root, "_products");
        Directory.CreateDirectory(media);
        var utc = new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.Zero);
        TelemetryProductStore.Save(_root, new TelemetryProductRecord(Product, "wav", "Fixture audio",
            1, 3, false, false, 1, utc, utc, 39444, "AO-73", "fixture", Payload.Length), Payload);
        TelemetryProductStore.Save(_root, new TelemetryProductRecord(Product, "wav", "Fixture audio",
            2, 3, false, true, 2, utc, utc.AddSeconds(1), 39444, "AO-73", "fixture", Payload.Length), Payload);
        info.Environment["SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY"] = _root;
    }

    internal async Task VerifyAsync(HttpClient anonymous, HttpClient authenticated)
    {
        string list = Route + "?norad=39444&day=" + Day + "&limit=1";
        string download = Route + "/39444/" + Day + "/" + Newer;
        using var denied = await anonymous.GetAsync(list);
        using var deniedFile = await anonymous.GetAsync(download + "/bin");
        if (denied.StatusCode != HttpStatusCode.Unauthorized || deniedFile.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidDataException("Anonymous archive access allowed.");
        using var first = JsonDocument.Parse(await authenticated.GetStringAsync(list));
        string? cursor = first.RootElement.GetProperty("next").GetString();
        if (cursor != Newer || first.RootElement.GetProperty("items")[0].GetProperty("stem").GetString() != Newer)
            throw new InvalidDataException("Archive page ordering or incomplete pair handling.");
        using var second = JsonDocument.Parse(await authenticated.GetStringAsync(list + "&before=" + cursor));
        if (second.RootElement.GetProperty("items")[0].GetProperty("stem").GetString() != Older ||
            second.RootElement.GetProperty("next").ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Archive cursor did not select older record.");
        if (!(await authenticated.GetByteArrayAsync(download + "/bin")).SequenceEqual(Payload) ||
            await authenticated.GetStringAsync(download + "/log") != "CRC/FEC: OK")
            throw new InvalidDataException("Archive download changed content.");
        using var invalid = await authenticated.GetAsync(Route + "?norad=39444&day=20260230");
        using var incomplete = await authenticated.GetAsync(Route + "/39444/" + Day + "/" + Incomplete + "/bin");
        if (invalid.StatusCode != HttpStatusCode.BadRequest || incomplete.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidDataException("Invalid or incomplete archive record exposed.");
        string product = "/api/v1/dsp/telemetry/product/" + Product.ToString("N") + "/wav";
        using var deniedProduct = await anonymous.GetAsync(product);
        using var receivedProduct = await authenticated.GetAsync(product);
        if (deniedProduct.StatusCode != HttpStatusCode.Unauthorized ||
            receivedProduct.Content.Headers.ContentType?.MediaType != "audio/wav" ||
            !(await receivedProduct.Content.ReadAsByteArrayAsync()).SequenceEqual(Payload))
            throw new InvalidDataException("Media product authentication or content delivery failed.");
        const string catalog = "/api/v1/dsp/telemetry/products?norad=39444&day=20261125";
        using var deniedCatalog = await anonymous.GetAsync(catalog);
        using var catalogJson = JsonDocument.Parse(await authenticated.GetStringAsync(catalog));
        var item = catalogJson.RootElement.GetProperty("items").EnumerateArray().Single();
        if (deniedCatalog.StatusCode != HttpStatusCode.Unauthorized || item.GetProperty("revision").GetInt32() != 2 ||
            !item.GetProperty("final").GetBoolean() || item.GetProperty("complete").GetBoolean())
            throw new InvalidDataException("Progressive media history did not retain one final revision.");
        Console.WriteLine("PASS: authenticated archive pagination, exact BIN/LOG/media download and durable progressive media catalog.");
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
