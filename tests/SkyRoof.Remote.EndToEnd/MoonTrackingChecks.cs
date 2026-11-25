using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Host.Realtime;

internal static class MoonTrackingChecks
{
    internal static async Task RunAsync(HubConnection hub, string stationDll, FakeRigctld radio)
    {
        if (Environment.GetEnvironmentVariable("SKYROOF_TEST_ORBIT_BACKEND") == "sgpnet" ||
            !File.Exists(Path.Combine(Path.GetDirectoryName(stationDll) ?? "", "orekit-data", "lnxp1990.430"))) return;
        var request = new RemoteTrackingRequest { CelestialTarget = "MOON", EmeDopplerMode = "cfom",
            LatitudeDegrees = 28, LongitudeDegrees = 116, HeightMetres = 40,
            UtcUnixMilliseconds = new DateTimeOffset(2026, 10, 28, 19, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            DownlinkHz = 432080000, UplinkHz = 432080000, CorrectDownlink = true, CorrectUplink = true };
        var moon = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request);
        var earlier = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request with { UtcUnixMilliseconds = request.UtcUnixMilliseconds - 1000 });
        var later = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request with { UtcUnixMilliseconds = request.UtcUnixMilliseconds + 1000 });
        double factor = moon.RangeRateMetresPerSecond / 299792458.0;
        if (moon.CelestialTarget != "MOON" || moon.RangeMetres is < 340e6 or > 420e6 ||
            moon.OwnEchoDelaySeconds is < 2.2 or > 2.9 ||
            Math.Abs((later.RangeMetres - earlier.RangeMetres) / 2 - moon.RangeRateMetresPerSecond) > 3 ||
            moon.RxHz != (long)Math.Round(request.DownlinkHz * (1 - factor)) ||
            moon.TxHz != (long)Math.Round(request.UplinkHz * (1 + factor)))
            throw new InvalidDataException("JPL Moon range, rate, or CFOM correction invalid.");
        var echo = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request with { EmeDopplerMode = "own-echo-rx" });
        if (echo.TxHz != request.UplinkHz || echo.RxHz != (long)Math.Round(request.DownlinkHz * (1 - 2 * factor)))
            throw new InvalidDataException("Moon own-echo correction invalid.");
        await MoonDxTrackingChecks.RunAsync(hub, request);
        int ptt = radio.Count("T 1");
        var applied = await hub.InvokeAsync<RemoteTrackingStatus>("ApplyTracking", request with { ElevationMaskDegrees = -90 });
        if (!applied.CatApplied || applied.ActualRxHz != moon.RxHz || applied.ActualTxHz != moon.TxHz || radio.Count("T 1") != ptt)
            throw new InvalidDataException("Moon CAT correction failed or keyed PTT.");
        Console.WriteLine("PASS: packaged JPL DE430 Moon position/range/range-rate, CFOM and own-echo Doppler, real CAT readback without PTT.");
    }
}
