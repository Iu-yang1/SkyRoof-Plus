using System.Text.Json;
using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public static class Q65Endpoints
{
    public static void MapQ65(this WebApplication app)
    {
        app.MapGet("/api/v1/dsp/q65", ReadAsync).RequireAuthorization();
        app.MapPut("/api/v1/dsp/q65", ConfigureAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));
    }

    private static async Task<IResult> ReadAsync(HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        if (!OperatingSystem.IsWindows()) return Results.StatusCode(501);
        string name = Environment.GetEnvironmentVariable("SKYROOF_Q65_RESULTS_BUS") ?? "skyroof-q65-results";
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
        { return Results.Problem(title: "Q65 DSP not available", statusCode: 503); }
    }

    private static async Task<IResult> ConfigureAsync(Q65RxConfiguration config, HttpContext context, DspWaveformBridge dsp)
    {
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) return Results.Forbid();
        try { return Results.Ok(await dsp.ConfigureQ65Async(config, context.RequestAborted)); }
        catch (RpcException error)
        {
            return Results.Problem(title: "Q65 configuration failed", detail: error.Status.Detail,
                statusCode: error.StatusCode == StatusCode.InvalidArgument ? 400 : 503);
        }
    }
}
