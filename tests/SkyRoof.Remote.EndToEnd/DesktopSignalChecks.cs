using Google.Protobuf;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;
using SkyRoof.RemoteDesktop;

internal static class DesktopSignalChecks
{
    internal static async Task RunAsync(string pipe, string bus)
    {
        using var client = new StationIpcClient(pipe);
        using var ring = new NamedSignalRing(bus, 64, 4096, 10);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        using var stream = client.Subscribe(StationSignalKind.StationSignalRxAudio, token);
        byte[] pcm = Enumerable.Range(0, 1920).Select(x => (byte)(x % 251)).ToArray();
        ring.Publish(960, System.Diagnostics.Stopwatch.GetTimestamp(), 48000, 960, pcm);
        Require(await stream.ResponseStream.MoveNext(token), "Station RX IPC returned no frame.");
        var first = stream.ResponseStream.Current;
        Require(first.Epoch == 10 && first.Channels == 1 && first.FirstSampleIndex == 960 &&
            first.Payload.Span.SequenceEqual(pcm), "Station RX IPC changed PCM or sample timing.");
        ring.ResetEpoch(11);
        ring.Publish(0, System.Diagnostics.Stopwatch.GetTimestamp(), 48000, 960, pcm);
        Require(await stream.ResponseStream.MoveNext(token) && stream.ResponseStream.Current.Epoch == 11 &&
            stream.ResponseStream.Current.Sequence == 0, "Station RX IPC failed epoch recovery.");
        Console.WriteLine("PASS: native desktop station streaming IPC preserves PCM, index, epoch and producer restart.");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
