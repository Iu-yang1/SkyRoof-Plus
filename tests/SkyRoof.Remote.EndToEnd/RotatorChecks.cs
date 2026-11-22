using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Host.Realtime;

internal static class RotatorChecks
{
    internal static async Task RunAsync(HubConnection operatorHub, FakeRigctld hardware)
    {
        var initial = await operatorHub.InvokeAsync<RemoteRotatorPosition>("GetRotatorPosition");
        Check(double.IsFinite(initial.AzimuthDegrees) && double.IsFinite(initial.ElevationDegrees),
            "Rotator p readback invalid.");
        var pointCount = hardware.CountPrefix("P ");
        var point = await operatorHub.InvokeAsync<RemoteRotatorPosition>("PointRotator", 90.0, 30.0);
        Check(Math.Abs(point.AzimuthDegrees - 90) < .001 &&
            Math.Abs(point.ElevationDegrees - 30) < .001 &&
            hardware.CountPrefix("P ") == pointCount + 1 &&
            hardware.Count("P 90.000 30.000") == 1,
            "Operator rotator command did not reach real TCP rotctld backend.");
        var stops = hardware.Count("S");
        var stopped = await operatorHub.InvokeAsync<RemoteRotatorPosition>("StopRotator");
        Check(hardware.Count("S") == stops + 1 &&
            Math.Abs(stopped.AzimuthDegrees - 90) < .001,
            "Operator STOP command was not transmitted.");
        var blocked = false;
        try { await operatorHub.InvokeAsync<RemoteRotatorPosition>("PointRotator", 451, 30); }
        catch (Microsoft.AspNetCore.SignalR.HubException) { blocked = true; }
        Check(blocked && hardware.CountPrefix("P ") == pointCount + 1,
            "Out-of-range rotation crossed configured mechanical limit.");
        Console.WriteLine("PASS: authorized rotctld point/position/STOP with physical-limit and Viewer guards.");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
