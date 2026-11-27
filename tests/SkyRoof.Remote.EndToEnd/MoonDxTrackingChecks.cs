using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Host.Realtime;

internal static class MoonDxTrackingChecks
{
    internal static async Task RunAsync(HubConnection hub, RemoteTrackingRequest local)
    {
        var request = local with { EmeDopplerMode = "dx-fixed", EmePeer = new RemoteEmePeer(47, 8, 500) };
        var center = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request);
        var before = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking",
            request with { UtcUnixMilliseconds = request.UtcUnixMilliseconds - 500 });
        var after = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking",
            request with { UtcUnixMilliseconds = request.UtcUnixMilliseconds + 500 });
        var path = center.EmePath ?? throw new InvalidDataException("DX path missing.");
        var a = before.EmePath ?? throw new InvalidDataException("Earlier DX path missing.");
        var b = after.EmePath ?? throw new InvalidDataException("Later DX path missing.");
        double rxFromDelay = 1 - (b.IncomingDelaySeconds - a.IncomingDelaySeconds);
        double txFromDelay = 1 / (1 + b.OutgoingDelaySeconds - a.OutgoingDelaySeconds);
        if (path.IncomingDelaySeconds is < 2.2 or > 2.9 ||
            path.OutgoingDelaySeconds is < 2.2 or > 2.9 ||
            Math.Abs(path.ReceiveRatio - rxFromDelay) > 2e-9 ||
            Math.Abs(path.TransmitRatio - txFromDelay) > 2e-9 ||
            center.RxHz != (long)Math.Round(request.DownlinkHz * path.ReceiveRatio) ||
            center.TxHz != (long)Math.Round(request.UplinkHz / path.TransmitRatio))
            throw new InvalidDataException("Two-leg EME light-time frequency ratios disagree with numerical derivative or CAT frequencies.");
        Console.WriteLine("PASS: two-station retarded/advanced DE430 light-time and Doppler independently verified by path-delay derivative.");
    }
}
