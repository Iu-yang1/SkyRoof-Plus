using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Orbit.SgpNet;
using SkyRoof.Remote.Host.Realtime;

internal static class ContinuousTrackingChecks
{
    private static RemoteTrackingRequest CurrentFixture()
    {
        var input = TrackingChecks.Request();
        var utc = DateTimeOffset.UtcNow;
        var epochDay = utc.DayOfYear + utc.TimeOfDay.TotalDays;
        var line = input.TleLine1[..18] + utc.ToString("yy", CultureInfo.InvariantCulture)
            + epochDay.ToString("000.00000000", CultureInfo.InvariantCulture)
            + input.TleLine1[32..68];
        var checksum = line.Sum(c => char.IsAsciiDigit(c) ? c - '0' : c == '-' ? 1 : 0) % 10;
        line += checksum.ToString(CultureInfo.InvariantCulture);
        var site = new SgpNetOrbitBackend().CreateTleTrack(line, input.TleLine2)
            .PredictGeodetic(utc);
        return input with
        {
            TleLine1 = line,
            LatitudeDegrees = site.LatitudeDegrees,
            LongitudeDegrees = ((site.LongitudeDegrees + 185) % 360 + 360) % 360 - 180,
            UtcUnixMilliseconds = utc.ToUnixTimeMilliseconds()
        };
    }

    internal static async Task<Process> RunAsync(Func<HubConnection> connect, Process station,
        ProcessStartInfo stationInfo, FakeRigctld radio, FakeRigctld rotator, string jobFile)
    {
        var request = CurrentFixture();
        await using var first = connect();
        await first.StartAsync();
        var job = await first.InvokeAsync<RemoteTrackingJobStatus>(
            "StartTrackingJob", request, 300, true);
        var before = await WaitAsync(first, x => x.CompletedTicks >= 2 && x.Phase == "tracking");
        Check(File.Exists(jobFile) && before.JobId == job.JobId, "Tracking job not saved");
        before = await VerifyLiveTuneAsync(first, before);
        await first.StopAsync();
        await Task.Delay(1300);
        await using var observer = connect();
        await observer.StartAsync();
        var after = await WaitAsync(observer, x => x.CompletedTicks > before.CompletedTicks);
        Check(after.Latest is not null && before.Latest is not null &&
            after.Latest.UtcUnixMilliseconds > before.Latest.UtcUnixMilliseconds &&
            (after.Latest.RxHz != before.Latest.RxHz || after.Latest.TxHz != before.Latest.TxHz),
            "Tracking stalled or reused historical Doppler after client disconnect");

        station.Kill(entireProcessTree: true);
        await station.WaitForExitAsync();
        var replacement = Process.Start(stationInfo)
            ?? throw new InvalidOperationException("Station restart failed");
        try
        {
            var resumed = await WaitAsync(observer, x => x.JobId == job.JobId &&
                x.Running && x.CompletedTicks >= 2 && x.Phase == "tracking");
            Check(resumed.ResumeAfterRestart, "Authorized tracking job not restored");
            VerifyTunedReadback(resumed);
            var stale = await observer.InvokeAsync<RemoteTrackingJobStatus>(
                "StopTrackingJob", "old-job");
            Check(stale.Running && stale.JobId == job.JobId, "Stale stop canceled current job");
            var stopped = await observer.InvokeAsync<RemoteTrackingJobStatus>(
                "StopTrackingJob", job.JobId);
            Check(!stopped.Running && stopped.Phase == "stopped" && !File.Exists(jobFile),
                "Tracking stop failed to clear durable task");
            var pointCount = rotator.CountPrefix("P ");
            var tuneCount = radio.CountPrefix("F ");
            await Task.Delay(1100);
            var final = await observer.InvokeAsync<RemoteTrackingJobStatus>("GetTrackingJob");
            Check(final.CompletedTicks == stopped.CompletedTicks &&
                rotator.CountPrefix("P ") == pointCount && radio.CountPrefix("F ") == tuneCount,
                "Device writes continued after STOP");
            Check(rotator.Count("S") >= 1 && radio.Count("T 1") == 1,
                "Tracking stop failed to stop rotator or tracking keyed TX");
            Console.WriteLine("PASS: continuous Station UTC Doppler/AZ-EL, client disconnect, process restart, stale STOP and durable stop");
            return replacement;
        }
        catch
        {
            if (!replacement.HasExited) replacement.Kill(entireProcessTree: true);
            await replacement.WaitForExitAsync();
            replacement.Dispose();
            throw;
        }
    }

    private static async Task<RemoteTrackingJobStatus> VerifyLiveTuneAsync(HubConnection hub, RemoteTrackingJobStatus before)
    {
        var result = await hub.InvokeAsync<RemoteTrackingJobStatus>("TuneTrackingJob",
            new RemoteTrackingTune { JobId = before.JobId, RitHz = 12000, XitHz = -9000 });
        Check(result.ActiveRadioLink is { RitHz: 12000, XitHz: -9000 } && result.PointRotator,
            "Station did not return accepted RadioLink offsets.");
        Check(result.JobId == before.JobId && result.Running && result.CompletedTicks >= before.CompletedTicks,
            "Live tune restarted or replaced tracking job.");
        var tuned = await WaitAsync(hub, x => x.CompletedTicks > result.CompletedTicks && x.Phase == "tracking");
        VerifyTunedReadback(tuned);
        try
        {
            await hub.InvokeAsync<RemoteTrackingJobStatus>("TuneTrackingJob", new RemoteTrackingTune { JobId = "old-job", RitHz = 0 });
            throw new InvalidDataException("Stale tune accepted.");
        }
        catch (HubException) { Console.WriteLine("PASS: live RIT/XIT and stale tracking job rejection."); }
        return tuned;
    }

    private static void VerifyTunedReadback(RemoteTrackingJobStatus status)
    {
        Check(status.ActiveRadioLink is { RitHz: 12000, XitHz: -9000 }, "Active RadioLink readback lost after reconnect/restart.");
        var current = status.Latest ?? throw new InvalidDataException("Missing tuned prediction.");
        var factor = current.RangeRateMetresPerSecond / 299792458.0;
        long rx = (long)Math.Round((145900000 + 12000) * (1 - factor));
        long tx = (long)Math.Round(435650000 * (1 + factor) - 9000);
        Check(current.RxHz == rx && current.TxHz == tx && current.ActualRxHz == rx && current.ActualTxHz == tx,
            "Live offsets not applied to actual CAT or not restored after restart.");
    }

    private static async Task<RemoteTrackingJobStatus> WaitAsync(
        HubConnection hub, Func<RemoteTrackingJobStatus, bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                var status = await hub.InvokeAsync<RemoteTrackingJobStatus>("GetTrackingJob",
                    deadline.Token);
                if (condition(status)) return status;
            }
            catch (HubException) when (!deadline.IsCancellationRequested) { }
            await Task.Delay(200, deadline.Token);
        }
        throw new TimeoutException("Tracking job never reached expected status.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
