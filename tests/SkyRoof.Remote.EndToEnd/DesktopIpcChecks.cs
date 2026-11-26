using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;

internal static class DesktopIpcChecks
{
    internal static async Task RunAsync(string pipe, FakeRigctld radio, FakeRigctld rotator)
    {
        using var client = new StationIpcClient(pipe);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var initial = await client.ReadRadioAsync(token);
        Require(initial.RxFrequencyHz == 145_900_000 && !initial.ActualPtt, "Desktop readback mismatch.");
        var tuned = await client.SetRadioAsync(DeviceOperation.DeviceRxFrequency, 145_901_000, "", token);
        Require(tuned.RxFrequencyHz == 145_901_000 && radio.Count("F 145901000") == 1, "Desktop tuning did not reach TCP CAT.");
        var restored = await client.SetRadioAsync(DeviceOperation.DeviceRxFrequency, initial.RxFrequencyHz, "", token);
        Require(restored.RxFrequencyHz == initial.RxFrequencyHz, "Desktop restore failed.");
        var position = await client.PointAsync(12, 5, token);
        Require(position.HasPosition && position.ReadbackFresh && rotator.Count("P 12.000 5.000") == 1,
            "Desktop rotator control did not reach TCP hardware fixture.");
        var stops = rotator.Count("S");
        var stopped = await client.StopAsync(token);
        Require(stopped.StopTransmitted && rotator.Count("S") == stops + 1, "Desktop STOP missing.");
        _ = await client.TrackingAsync(token);
        Console.WriteLine("PASS: original WinForms IPC adapter uses actual Station and Device processes for CAT/rotator/tracking.");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
