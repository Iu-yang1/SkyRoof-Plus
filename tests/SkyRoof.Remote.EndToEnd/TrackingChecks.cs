using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Orbit.SgpNet;
using SkyRoof.Remote.Host.Realtime;

internal static class TrackingChecks
{
    private const string Tle1 = "1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753";
    private const string Tle2 = "2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667";
    private const double C = 299_792_458.0;

    internal static RemoteTrackingRequest Request()
    {
        var epoch = new DateTimeOffset(2000, 6, 27, 18, 50, 20, TimeSpan.Zero);
        // Use an off-nadir ground site so both elevation and range rate have
        // physically meaningful, nondegenerate values at the known TLE epoch.
        var subpoint = new SgpNetOrbitBackend().CreateTleTrack(Tle1, Tle2)
            .PredictGeodetic(epoch);
        return new RemoteTrackingRequest(Tle1, Tle2, subpoint.LatitudeDegrees,
            ((subpoint.LongitudeDegrees + 185) % 360 + 360) % 360 - 180, 40,
            epoch.ToUnixTimeMilliseconds(), 145_900_000, 435_650_000,
            true, true, true, 0);
    }

    internal static async Task RunAsync(HubConnection hub, FakeRigctld radio, FakeRigctld rotator)
    {
        var request = Request();
        var prediction = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request);
        Check(prediction.NoradNumber == 5 && prediction.OrbitBackend == "sgpnet"
            && prediction.ElevationDegrees > 0 && prediction.RangeMetres > 0
            && !prediction.CatApplied && !prediction.RotatorApplied, "Invalid orbit prediction");
        var earlier = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking",
            request with { UtcUnixMilliseconds = request.UtcUnixMilliseconds - 1000 });
        var later = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking",
            request with { UtcUnixMilliseconds = request.UtcUnixMilliseconds + 1000 });
        var derivative = (later.RangeMetres - earlier.RangeMetres) / 2;
        Check(Math.Abs(derivative - prediction.RangeRateMetresPerSecond) < 3,
            "Range rate disagrees with finite-difference geometric range");
        var factor = prediction.RangeRateMetresPerSecond / C;
        Check(prediction.RxHz == (long)Math.Round(request.DownlinkHz * (1 - factor)) &&
            prediction.TxHz == (long)Math.Round(request.UplinkHz * (1 + factor)),
            "Incorrect duplex Doppler signs or magnitudes");

        var blocked = await hub.InvokeAsync<RemoteTrackingStatus>("ApplyTracking",
            request with { ElevationMaskDegrees = 90 });
        Check(!blocked.CatApplied && !blocked.RotatorApplied, "Elevation mask ignored");
        var applied = await hub.InvokeAsync<RemoteTrackingStatus>("ApplyTracking", request);
        Check(applied.CatApplied && applied.RotatorApplied &&
            applied.ActualRxHz == prediction.RxHz && applied.ActualTxHz == prediction.TxHz,
            "CAT duplex/rotator application failed");
        Check(Math.Abs(applied.ActualAzimuthDegrees.GetValueOrDefault(double.NaN) -
            prediction.AzimuthDegrees) < 0.001 &&
            Math.Abs(applied.ActualElevationDegrees.GetValueOrDefault(double.NaN) -
            prediction.ElevationDegrees) < 0.001, "Rotator readback mismatch");
        Check(radio.Count(FormattableString.Invariant($"F {prediction.RxHz}")) == 1 &&
            radio.Count(FormattableString.Invariant($"I {prediction.TxHz}")) == 1 &&
            rotator.Count(FormattableString.Invariant($"P {prediction.AzimuthDegrees:F3} {prediction.ElevationDegrees:F3}")) == 1,
            "Expected actual TCP commands missing");
        Check(radio.Count("T 1") == 1, "Orbital tracking unexpectedly keyed transmitter");
        Console.WriteLine(FormattableString.Invariant(
            $"PASS: TLE -> SGP.NET -> range-rate {prediction.RangeRateMetresPerSecond:F3} m/s -> RX {prediction.RxHz} TX {prediction.TxHz} -> rotctld AZ {prediction.AzimuthDegrees:F3} EL {prediction.ElevationDegrees:F3}"));
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
