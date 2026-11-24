using System.Net;
using SkyRoof.Orbit.Orekit;
using SkyRoof.Remote.Station;

internal static class IssOemCacheSuite
{
    private sealed class FixtureHttp(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    internal static async Task RunAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "iss-oem-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(folder, "iss.oem");
        var utc = new DateTimeOffset(2026, 10, 8, 0, 1, 0, TimeSpan.Zero);
        var uri = new Uri("https://fixture.invalid/iss.oem");
        try
        {
            using var valid = new HttpClient(new FixtureHttp(IssOemSuite.Fixture));
            await IssOemCacheRefresh.RefreshAsync(valid, uri, target, utc, CancellationToken.None);
            var original = File.ReadAllBytes(target);
            var track = new OrekitIssOemTrack(target);
            if (!track.Covers(utc) || track.HasFutureCoverage(utc.AddHours(1)))
                throw new InvalidDataException("Cached OEM coverage is incorrect.");

            await ExpectRejectedAsync(new FixtureHttp("<html>not OEM</html>"), uri, target, utc);
            await ExpectRejectedAsync(new FixtureHttp("unavailable", HttpStatusCode.ServiceUnavailable),
                uri, target, utc);
            await ExpectRejectedAsync(new FixtureHttp(IssOemSuite.Fixture), uri, target, utc.AddDays(1));

            if (!original.SequenceEqual(File.ReadAllBytes(target)) || Directory.GetFiles(folder).Length != 1)
                throw new InvalidDataException("Failed OEM refresh replaced the last good file or left a partial file.");
            Console.WriteLine("PASS: ISS OEM HTTP refresh validates before atomic replacement; invalid, expired and failed responses retain previous cache.");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    private static async Task ExpectRejectedAsync(HttpMessageHandler handler, Uri uri, string target,
        DateTimeOffset utc)
    {
        using var client = new HttpClient(handler);
        bool rejected = false;
        try { await IssOemCacheRefresh.RefreshAsync(client, uri, target, utc, CancellationToken.None); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException ||
            ex.GetType().FullName?.StartsWith("org.orekit.", StringComparison.Ordinal) == true)
        { rejected = true; }
        if (!rejected) throw new InvalidDataException("Invalid OEM refresh was accepted.");
    }
}
