using System.Diagnostics;
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
        var parkStart = hardware.CountPrefix("P ");
        var parked = await operatorHub.InvokeAsync<RemoteRotatorPosition>("ParkRotator",
            new[] { new RemoteParkWaypoint(90, 0), new RemoteParkWaypoint(0, 0) });
        Check(parked.ReadbackFresh && parked.HasPosition &&
            Math.Abs(parked.AzimuthDegrees) < .001 &&
            Math.Abs(parked.ElevationDegrees) < .001 &&
            hardware.CountPrefix("P ") == parkStart + 2 &&
            hardware.Count("P 90.000 0.000") >= 1 &&
            hardware.Count("P 0.000 0.000") >= 1,
            "Multi-waypoint PARK did not wait for and verify real rotator position.");

        // One client is waiting for delayed p feedback; STOP must use its own
        // rotctld session and not queue for the delayed response.
        var priorQueries = hardware.Count("p");
        hardware.DelayPositionReadback(2300);
        var inFlight = operatorHub.InvokeAsync<RemoteRotatorPosition>("PointRotator", 120, 40);
        var pendingDeadline = Stopwatch.StartNew();
        while (hardware.Count("p") <= priorQueries &&
            pendingDeadline.Elapsed < TimeSpan.FromSeconds(3))
            await Task.Delay(25);
        Check(hardware.Count("p") > priorQueries, "Simulated position read never began.");
        var urgentClock = Stopwatch.StartNew();
        var immediateStop = await operatorHub.InvokeAsync<RemoteRotatorPosition>("StopRotator");
        urgentClock.Stop();
        Check(immediateStop.StopTransmitted && !immediateStop.ReadbackFresh &&
            immediateStop.HasPosition && urgentClock.Elapsed < TimeSpan.FromMilliseconds(1400),
            $"STOP was blocked by pending feedback: {urgentClock.ElapsedMilliseconds} ms.");
        var aborted = false;
        try { await inFlight; }
        catch (Microsoft.AspNetCore.SignalR.HubException) { aborted = true; }
        hardware.DelayPositionReadback(0);
        Check(aborted, "Late stale POINT was allowed to confirm after STOP.");
        var invalidParkCount = hardware.CountPrefix("P ");
        var invalidParkRejected = false;
        try
        {
            await operatorHub.InvokeAsync<RemoteRotatorPosition>("ParkRotator",
                new[] { new RemoteParkWaypoint(180, 10), new RemoteParkWaypoint(451, 0) });
        }
        catch (Microsoft.AspNetCore.SignalR.HubException) { invalidParkRejected = true; }
        Check(invalidParkRejected && hardware.CountPrefix("P ") == invalidParkCount,
            "Invalid later PARK waypoint was accepted after earlier movement began.");

        // STOP interrupts a multi-segment PARK before any later waypoint can run.
        var previousFinalWaypoint = hardware.Count("P 0.000 0.000");
        var parkQueries = hardware.Count("p");
        hardware.DelayPositionReadback(2300);
        var pendingPark = operatorHub.InvokeAsync<RemoteRotatorPosition>("ParkRotator",
            new[] { new RemoteParkWaypoint(175, 45), new RemoteParkWaypoint(0, 0) });
        var waitPark = Stopwatch.StartNew();
        while (hardware.Count("p") <= parkQueries && waitPark.Elapsed < TimeSpan.FromSeconds(3))
            await Task.Delay(25);
        Check(hardware.Count("p") > parkQueries, "PARK position read never began.");
        var stopClock = Stopwatch.StartNew();
        var stopDuringPark = await operatorHub.InvokeAsync<RemoteRotatorPosition>("StopRotator");
        stopClock.Stop();
        Check(stopDuringPark.StopTransmitted &&
            stopClock.Elapsed < TimeSpan.FromMilliseconds(1400),
            "PARK STOP waited behind delayed feedback.");
        var parkAborted = false;
        try { await pendingPark; }
        catch (Microsoft.AspNetCore.SignalR.HubException) { parkAborted = true; }
        hardware.DelayPositionReadback(0);
        Check(parkAborted && hardware.Count("P 0.000 0.000") == previousFinalWaypoint,
            "Superseded PARK continued to a later waypoint after STOP.");
        Console.WriteLine("PASS: multi-segment PARK validation and priority STOP preemption.");
        Console.WriteLine("PASS: multi-waypoint PARK follows actual AZ/EL feedback.");
        Console.WriteLine("PASS: urgent STOP preempts blocked feedback and rejects superseded move.");
        Console.WriteLine("PASS: authorized rotctld point/position/STOP with physical-limit and Viewer guards.");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
