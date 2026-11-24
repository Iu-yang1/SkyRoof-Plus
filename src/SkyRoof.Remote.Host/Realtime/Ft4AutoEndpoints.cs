using Grpc.Core;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record Ft4AutoRequest
{
    public string Call { get; init; } = "";
    public string Grid { get; init; } = "";
    public string Target { get; init; } = "";
    public int Attempts { get; init; } = 4;
    public long FrequencyHz { get; init; }
    public float AudioHz { get; init; } = 1500;
    public float Gain { get; init; } = 0.5f;
    public bool Odd { get; init; }
}

public static class Ft4AutoEndpoints
{
    private static readonly SemaphoreSlim Owner = new(1, 1);

    public static void MapAutoFt4(this WebApplication app) =>
        app.MapPost("/api/v1/dsp/ft4/auto", RunAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));

    private static async Task RunAsync(Ft4AutoRequest request, HttpContext context,
        DspWaveformBridge dsp, StationDeviceBridge station)
    {
        if (!OperatingSystem.IsWindows()) { context.Response.StatusCode = 501; return; }
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) { context.Response.StatusCode = 403; return; }
        Ft4QsoSequence qso;
        try
        {
            qso = new(request.Call ?? "", request.Grid ?? "", request.Target ?? "", request.Attempts);
            if (request.FrequencyHz <= 0 || !float.IsFinite(request.AudioHz) || request.AudioHz is <= 0 or >= 24000 ||
                !float.IsFinite(request.Gain) || request.Gain is < 0 or > 1)
                throw new ArgumentException("Invalid FT4 frequency or amplitude.");
        }
        catch (ArgumentException error)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
            return;
        }
        if (!await Owner.WaitAsync(0, context.RequestAborted))
        { context.Response.StatusCode = 409; return; }
        try
        {
            string name = Environment.GetEnvironmentVariable("SKYROOF_FT4_RESULTS_BUS") ?? "skyroof-ft4-results";
            using var ring = new NamedSignalRing(name, 32, 32768);
            context.Response.ContentType = "application/x-ndjson";
            context.Response.Headers.CacheControl = "no-store";
            var session = new Ft4AutoSession(context, request, dsp, station);
            await session.RunAsync(qso, ring);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (error is RpcException or InvalidDataException or IOException or InvalidOperationException)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 409;
            else await Ft4AutoSession.WriteErrorAsync(context, error.Message);
        }
        finally { Owner.Release(); }
    }
}
