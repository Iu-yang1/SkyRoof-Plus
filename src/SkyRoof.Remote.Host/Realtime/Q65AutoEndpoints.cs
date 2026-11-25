using Grpc.Core;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record Q65AutoRequest
{
    public string Call { get; init; } = "";
    public string Grid { get; init; } = "";
    public string Target { get; init; } = "";
    public int Attempts { get; init; } = 4;
    public long FrequencyHz { get; init; }
    public float Gain { get; init; } = 0.5f;
    public bool Odd { get; init; }
    public Q65RxConfiguration Rx { get; init; } = new();
}

public static class Q65AutoEndpoints
{
    public static void MapAutoQ65(this WebApplication app) =>
        app.MapPost("/api/v1/dsp/q65/auto", RunAsync)
            .RequireAuthorization(policy => policy.RequireRole("Operator", "Administrator"));

    private static async Task RunAsync(Q65AutoRequest request, HttpContext context,
        DspWaveformBridge dsp, StationDeviceBridge station)
    {
        if (!OperatingSystem.IsWindows()) { context.Response.StatusCode = 501; return; }
        if (!await TimedFt4Endpoints.IsOperatorAsync(context)) { context.Response.StatusCode = 403; return; }
        string call = (request.Call ?? "").Trim().ToUpperInvariant();
        string target = (request.Target ?? "").Trim().ToUpperInvariant();
        Ft4QsoSequence qso;
        try
        {
            qso = Ft4QsoSequence.ForQ65(call, request.Grid ?? "", target, request.Attempts);
            if (request.Rx is null || request.FrequencyHz <= 0 ||
                !float.IsFinite(request.Gain) || request.Gain is < 0 or > 1)
                throw new ArgumentException("Q65 automatic TX requires a receiver profile, RF frequency and PCM level.");
        }
        catch (ArgumentException error)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = error.Message });
            return;
        }
        if (!await DigitalAutoOwner.Gate.WaitAsync(0, context.RequestAborted))
        { context.Response.StatusCode = 409; return; }
        try
        {
            var config = request.Rx.Clone();
            config.Enabled = true;
            config.MyCall = call;
            config.HisCall = target;
            await dsp.ConfigureQ65Async(config, context.RequestAborted);
            string name = Environment.GetEnvironmentVariable("SKYROOF_Q65_RESULTS_BUS") ?? "skyroof-q65-results";
            using var ring = new NamedSignalRing(name, 32, 32768);
            context.Response.ContentType = "application/x-ndjson";
            context.Response.Headers.CacheControl = "no-store";
            await new Q65AutoSession(context, request, dsp, station).RunAsync(qso, config, ring);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (error is RpcException or InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 409;
                await context.Response.WriteAsJsonAsync(new { error = error.Message });
            }
            else await Ft4AutoSession.WriteErrorAsync(context, error.Message);
        }
        finally { DigitalAutoOwner.Gate.Release(); }
    }
}
