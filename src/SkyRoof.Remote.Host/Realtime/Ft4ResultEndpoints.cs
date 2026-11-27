using System.Text.Json;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public static class Ft4ResultEndpoints
{
    public static void MapFt4Results(this WebApplication app) =>
        app.MapGet("/api/v1/dsp/ft4", Read).RequireAuthorization();

    private static IResult Read()
    {
        if (!OperatingSystem.IsWindows()) return Results.StatusCode(501);
        string name = Environment.GetEnvironmentVariable("SKYROOF_FT4_RESULTS_BUS") ?? "skyroof-ft4-results";
        try
        {
            using var ring = new NamedSignalRing(name, 32, 32768);
            var cursor = new SignalCursor();
            var results = new List<JsonElement>();
            while (results.Count < 32 && ring.TryReadNext(cursor, out var frame) && frame is not null)
            {
                using var json = JsonDocument.Parse(frame.Payload);
                results.Add(json.RootElement.Clone());
            }
            return Results.Ok(new { epoch = ring.StreamEpoch, results });
        }
        catch (InvalidDataException)
        { return Results.Problem(title: "FT4 DSP not available", statusCode: 503); }
    }
}
