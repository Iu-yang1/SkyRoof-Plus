using System.Net;
using System.Text.Json;
using SkyRoof.Remote.Station;

internal static partial class CatalogOnlineFixture
{
    internal static async Task WriteSnapshotsAsync(string desktop)
    {
        byte[] local = File.ReadAllBytes(desktop);
        using var http = new HttpClient(new FixtureHttp());
        var satnogs = await CatalogOnlineFetcher.FetchSatnogsAsync(http,
            new Uri("https://fixture.invalid/api/"), CancellationToken.None);
        var celestrak = await CatalogOnlineFetcher.FetchCelestrakAsync(http,
            new Uri("https://fixture.invalid/gp.csv"), CancellationToken.None);
        if (satnogs.Length != 2 || celestrak.Length != 2 ||
            satnogs.Single(s => s.Norad == 100002).Transmitters.Single().UplinkLow != 435600000)
            throw new InvalidDataException("Online catalog pages or OMM rows are incomplete.");
        string path = desktop + ".satnogs.json";
        await CatalogOnlineFetcher.SaveAsync(path, satnogs, CancellationToken.None);
        await CatalogOnlineFetcher.SaveAsync(desktop + ".celestrak.json", celestrak, CancellationToken.None);
        var previous = File.ReadAllBytes(path);
        bool rejected = false;
        try { await CatalogOnlineFetcher.SaveAsync(path, [], CancellationToken.None); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected || !previous.SequenceEqual(File.ReadAllBytes(path)) ||
            !local.SequenceEqual(File.ReadAllBytes(desktop)))
            throw new InvalidDataException("Online refresh damaged desktop or last good snapshot.");
        Console.WriteLine("PASS: online SatNOGS pagination + CelesTrak CSV parse, independent atomic snapshots.");
    }

    internal static async Task VerifyStationAsync(HttpClient admin)
    {
        using var local = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/catalog/satellites?q=100001"));
        var record = local.RootElement.EnumerateArray().Single();
        var tx = record.GetProperty("transmitters").EnumerateArray().Single();
        if (record.GetProperty("orbitSource").GetString() != "OMM fixture" ||
            tx.GetProperty("link").GetProperty("downlinkLowHz").GetInt64() != 145900000)
            throw new InvalidDataException("Older online OMM or remote TX overrode newer/manual local data.");
        using var online = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/catalog/satellites?q=100002"));
        var incoming = online.RootElement.EnumerateArray().Single();
        var newTx = incoming.GetProperty("transmitters").EnumerateArray().Single();
        if (incoming.GetProperty("orbitState").GetString() != "omm-only" ||
            incoming.GetProperty("orbitSource").GetString() != "CelesTrak OMM CSV" ||
            newTx.GetProperty("link").GetProperty("uplinkLowHz").GetInt64() != 435600000)
            throw new InvalidDataException("Station did not combine online radio and orbital records.");
        Console.WriteLine("PASS: Station joins live sidecar radio/OMM, keeps local transmitter and newest orbit epoch.");
    }
}
