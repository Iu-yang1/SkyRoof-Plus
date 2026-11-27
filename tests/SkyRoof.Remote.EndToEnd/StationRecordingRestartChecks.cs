using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;
using SkyRoof.Remote.SignalBus;

internal static class StationRecordingRestartChecks
{
    internal static async Task<string> BeginAsync(string pipe)
    {
        using var client = new StationIpcClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var state = await client.StartStationRecordingAsync(StationSignalKind.StationSignalRxAudio, timeout.Token, true);
        if (!state.Running || !state.ResumeAfterRestart) throw new InvalidDataException("Recording intent not armed.");
        return state.Id;
    }

    internal static async Task VerifyAsync(string pipe, string bus, string directory, string id)
    {
        using var client = new StationIpcClient(pipe);
        using var ring = new NamedSignalRing(bus, 64, 4096, 71);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        var state = await client.StationRecordingAsync(token);
        if (!state.Running || !state.ResumeAfterRestart || state.Id != id)
            throw new InvalidDataException("Station recorder did not restore the durable task ID.");
        var pcm = new byte[1920];
        for (int i = 0; i < 50 && state.Samples == 0; i++)
        {
            ring.Publish((ulong)(i * 960), System.Diagnostics.Stopwatch.GetTimestamp(), 48000, 960, pcm);
            await Task.Delay(50, token);
            state = await client.StationRecordingAsync(token);
        }
        var stopped = await client.StopStationRecordingAsync(token);
        if (stopped.Running || stopped.ResumeAfterRestart || stopped.Samples <= 0 ||
            File.Exists(Path.Combine(directory, "recording-job.json")))
            throw new InvalidDataException("Restored recording did not capture or clear the durable intent on Stop.");
        Console.WriteLine("PASS: Station recording intent survives real process termination, resumes and clears on explicit Stop.");
    }
}
