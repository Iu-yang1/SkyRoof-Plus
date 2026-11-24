using System.Text.Json;
using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public static class CwResultEndpoints
{
    public sealed record Configuration(bool Enabled, string Mode);

    public static void MapCwResults(this WebApplication app)
    {
        app.MapGet("/api/v1/dsp/cw", Read).RequireAuthorization();
        app.MapPut("/api/v1/dsp/cw", ConfigureAsync)
            .RequireAuthorization(policy => policy.RequireRole("Administrator", "Operator"));
    }

    private static IResult Read()
    {
        if (!OperatingSystem.IsWindows()) return Results.StatusCode(501);
        string name = Environment.GetEnvironmentVariable("SKYROOF_CW_RESULTS_BUS") ?? "skyroof-cw-results";
        try
        {
            using var ring = new NamedSignalRing(name, 16, 32768);
            var cursor = new SignalCursor();
            SignalFrame? latest = null;
            while (ring.TryReadNext(cursor, out var frame) && frame is not null)
                latest = frame;
            if (latest is null) return Results.Ok(new { state = "waiting-for-audio" });
            using var json = JsonDocument.Parse(latest.Payload);
            return Results.Ok(json.RootElement.Clone());
        }
        catch (InvalidDataException)
        { return Results.Problem(title: "CW DSP not available", statusCode: 503); }
    }

    private static async Task<IResult> ConfigureAsync(Configuration request, HttpContext context,
        DspWaveformBridge dsp)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        try
        {
            var response = await dsp.ConfigureCwAsync(new CwRxConfiguration
            {
                Enabled = request.Enabled, Mode = request.Mode
            }, context.RequestAborted);
            return Results.Ok(new { enabled = response.Enabled, mode = response.Mode });
        }
        catch (RpcException e)
        { return Results.Problem(title: "CW configuration failed", detail: e.Status.Detail,
            statusCode: e.StatusCode == StatusCode.InvalidArgument ? 400 : 503); }
    }
}
