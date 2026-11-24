using System.Net;
using System.Text.Json;

internal static class CatalogChecks
{
    internal static void WriteFixture(string path)
    {
        var tle = TrackingChecks.Request();
        File.WriteAllText(path, JsonSerializer.Serialize(new[]
        {
            new
            {
                sat_id = "fixture-00005", norad_cat_id = 5, name = "Vanguard Test",
                names = "TEST-ALIAS", status = "in orbit",
                AutomaticTle = new { tle1 = tle.TleLine1, tle2 = tle.TleLine2, tle_source = "Vallado" },
                ManualTle = new { tle1 = "1 EXPIRED", tle2 = "2 EXPIRED", tle_source = "manual" },
                ManualTleExpiresUtc = DateTimeOffset.UtcNow.AddDays(-1),
                Transmitters = new[]
                {
                    new { uuid = "fixture-tx", description = "Inverting fixture", alive = true,
                        mode = "USB", uplink_mode = "LSB", status = "active",
                        downlink_low = 145900000L, downlink_high = 145920000L,
                        uplink_low = 435630000L, uplink_high = 435650000L, invert = true }
                }
            }
        }));
    }

    internal static async Task RunAsync(HttpClient anonymous, HttpClient admin)
    {
        const string path = "/api/v1/catalog/satellites?q=TEST-ALIAS";
        using var denied = await anonymous.GetAsync(path);
        if (denied.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidDataException("Unauthenticated catalog exposed.");
        using var response = await admin.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sat = json.RootElement.EnumerateArray().Single();
        var tx = sat.GetProperty("transmitters").EnumerateArray().Single();
        var link = tx.GetProperty("link");
        if (sat.GetProperty("norad").GetInt32() != 5 ||
            sat.GetProperty("tleLine1").GetString() != TrackingChecks.Request().TleLine1 ||
            sat.GetProperty("orbitSource").GetString() != "Vallado" ||
            !link.GetProperty("inverting").GetBoolean() ||
            link.GetProperty("uplinkHighHz").GetInt64() != 435650000)
            throw new InvalidDataException("Station catalog projection or manual orbit expiration incorrect.");
        Console.WriteLine("PASS: authenticated Host -> Station original-format catalog, alias search, expired manual orbit fallback, exact transmitter edges.");
    }
}
