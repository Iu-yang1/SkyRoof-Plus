using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;

internal static class PassPlanningChecks
{
    internal static async Task RunAsync(StationIpcClient client, FakeRigctld radio, FakeRigctld rotator, CancellationToken token)
    {
        var input = TrackingChecks.Request();
        var request = new TrackingRequest
        {
            TleLine1 = input.TleLine1, TleLine2 = input.TleLine2,
            LatitudeDegrees = input.LatitudeDegrees, LongitudeDegrees = input.LongitudeDegrees,
            HeightMetres = input.HeightMetres, DownlinkHz = input.DownlinkHz, UplinkHz = input.UplinkHz,
            UtcUnixMilliseconds = input.UtcUnixMilliseconds, CorrectDownlink = true, CorrectUplink = true,
            PointRotator = true
        };
        int cat = radio.CountPrefix("F "), point = rotator.CountPrefix("P ");
        var result = await client.PlanPassesAsync(new PassSearchRequest
        {
            Tracking = request, StartUtcMilliseconds = input.UtcUnixMilliseconds - 20 * 60_000,
            EndUtcMilliseconds = input.UtcUnixMilliseconds + 45 * 60_000
        }, token);
        var pass = result.Passes.Single(p => p.AosUtcMilliseconds <= input.UtcUnixMilliseconds && p.LosUtcMilliseconds >= input.UtcUnixMilliseconds);
        Require(pass.ActualAos && pass.ActualLos && pass.SkyPath.Count >= 31, "Pass boundary or path samples missing.");
        request.UtcUnixMilliseconds = pass.CulminationUtcMilliseconds;
        var peak = await client.PredictAsync(request, token);
        Require(Math.Abs(peak.ElevationDegrees - pass.MaximumElevationDegrees) < 0.02 &&
            pass.SkyPath.Any(p => p.UtcMilliseconds == pass.CulminationUtcMilliseconds), "Pass peak differs from Station prediction.");
        var ground = await client.GroundTrackAsync(new GroundTrackRequest
        {
            Tracking = request, StartUtcMilliseconds = input.UtcUnixMilliseconds,
            EndUtcMilliseconds = input.UtcUnixMilliseconds + 120000, StepSeconds = 30
        }, token);
        Require(ground.Points.Count == 5 && ground.Points.All(p => p.LatitudeDegrees is >= -90 and <= 90 &&
            double.IsFinite(p.LongitudeDegrees) && p.HeightMetres > 0 && p.OrbitBackend.Length > 0),
            "Ground track samples invalid.");
        Require(radio.CountPrefix("F ") == cat && rotator.CountPrefix("P ") == point, "Read-only pass search wrote hardware.");
        Console.WriteLine("PASS: native Station pass search, UTC AOS/LOS, maximum elevation and sky path; no CAT/rotator writes.");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
