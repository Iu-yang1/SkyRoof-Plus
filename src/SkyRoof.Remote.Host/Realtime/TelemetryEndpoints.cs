using System.Text.Json;
using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public static class TelemetryEndpoints
{
    public static void MapTelemetry(this WebApplication app)
    {
        app.MapGet("/api/v1/dsp/telemetry", (Delegate)ReadAsync).RequireAuthorization();
        app.MapPut("/api/v1/dsp/telemetry", ConfigureAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));
    }

    private static async Task<IResult> ReadAsync(HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        if (!OperatingSystem.IsWindows()) return Results.StatusCode(501);
        try
        {
            string name = Environment.GetEnvironmentVariable("SKYROOF_TELEMETRY_RESULTS_BUS") ?? "skyroof-telemetry-results";
            using var ring = new NamedSignalRing(name, 64, 262144);
            var cursor = new SignalCursor();
            var results = new List<JsonElement>();
            while (results.Count < 64 && ring.TryReadNext(cursor, out var frame) && frame is not null)
            {
                using var json = JsonDocument.Parse(frame.Payload);
                results.Add(json.RootElement.Clone());
            }
            return Results.Ok(new { epoch = ring.StreamEpoch, results });
        }
        catch (InvalidDataException) { return Results.Problem(title: "Telemetry DSP unavailable", statusCode: 503); }
    }

    private static async Task<IResult> ConfigureAsync(TelemetryRxConfiguration config, HttpContext context, DspWaveformBridge dsp)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        try { return Results.Ok(await dsp.ConfigureTelemetryAsync(config, context.RequestAborted)); }
        catch (RpcException e)
        { return Results.Problem(title: "Telemetry configuration failed", detail: e.Status.Detail,
            statusCode: e.StatusCode == StatusCode.InvalidArgument ? 400 : 503); }
    }
}
