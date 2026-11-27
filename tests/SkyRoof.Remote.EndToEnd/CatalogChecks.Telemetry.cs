using System.Text.Json;

internal static partial class CatalogChecks
{
    private static object TelemetryCatalogFixture() => new
    {
        sat_id = "fixture-telemetry", norad_cat_id = 39444, name = "AO-73 Profile Test", status = "in orbit",
        Transmitters = new object[]
        {
            new { uuid = "beacon", description = "1200 Bd BPSK beacon", mode = "BPSK",
                downlink_low = 145935000L, baud = 1200, alive = true },
            new { uuid = "unsupported", description = "BPSK AX.25", mode = "BPSK",
                downlink_low = 145950000L, baud = 1200, alive = true, gr_sats = new { framing = "AO-40 FEC short" } },
            new { uuid = "manual", description = "Test metadata", mode = "BPSK",
                downlink_low = 145955000L, baud = 1200, alive = true,
                gr_sats = new { modulation = "BPSK", framing = "CCSDS Concatenated", baudrate = 9600,
                    frame_size = 223, rs_basis = "dual", rs_interleaving = 2, scrambler = "CCSDS" },
                manual = new { baudrate = 4800, precoding = "differential", scrambler = "none" } }
        }
    };

    private static async Task VerifyTelemetryProfilesAsync(HttpClient admin)
    {
        using var response = await admin.GetAsync("/api/v1/catalog/satellites?q=AO-73%20Profile%20Test");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = json.RootElement.EnumerateArray().Single().GetProperty("transmitters").EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetString() ?? "");
        var beacon = entries["beacon"].GetProperty("telemetry");
        var manual = entries["manual"].GetProperty("telemetry");
        if (beacon.GetProperty("framing").GetString() != "AO40FEC" ||
            beacon.GetProperty("differentialMode").GetString() != "differential" ||
            entries["unsupported"].GetProperty("telemetry").ValueKind != JsonValueKind.Null ||
            manual.GetProperty("baud").GetDouble() != 4800 ||
            manual.GetProperty("framing").GetString() != "CCSDS" ||
            manual.GetProperty("convolutional").GetString() != "CCSDS" ||
            manual.GetProperty("frameSize").GetInt32() != 223 ||
            manual.GetProperty("rsInterleaving").GetInt32() != 2 ||
            manual.GetProperty("scramblerMode").GetString() != "off")
            throw new InvalidDataException("Catalog telemetry profile precedence or protocol classification failed.");
        Console.WriteLine("PASS: catalog telemetry profile AO-73, unsupported variant exclusion, manual baud/precoding and structured CCSDS metadata.");
    }
}
