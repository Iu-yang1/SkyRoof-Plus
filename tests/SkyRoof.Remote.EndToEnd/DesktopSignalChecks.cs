using Google.Protobuf;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;
using SkyRoof.RemoteDesktop;
using SkyRoof.Remote.SdrBridge;

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
        await CheckIqAsync(client, token);
        await CheckRawIqAsync(client, token);
        Console.WriteLine("PASS: native desktop station streaming IPC preserves PCM, index, epoch and producer restart, CF32 IQ bytes.");
    }

    private static async Task CheckIqAsync(StationIpcClient client, CancellationToken token)
    {
        using var iq = new NamedSignalRing("skyroof-iq-record", 64, 32768, 42);
        using var stream = client.Subscribe(StationSignalKind.StationSignalIqCf32, token);
        byte[] data = Enumerable.Range(0, 8192).Select(x => (byte)(x % 239)).ToArray();
        iq.Publish(1000, System.Diagnostics.Stopwatch.GetTimestamp(), 48000, 1024, data);
        Require(await stream.ResponseStream.MoveNext(token), "No CF32 Station frame.");
        var frame = stream.ResponseStream.Current;
        Require(frame.Channels == 2 && frame.SampleCount == 1024 && frame.FirstSampleIndex == 1000 &&
            frame.Payload.Span.SequenceEqual(data), "CF32 IQ recording transport changed sample data.");
    }

    private static async Task CheckRawIqAsync(StationIpcClient client, CancellationToken token)
    {
        using var tap = new RawIqTap("skyroof-raw-iq");
        using var stream = client.Subscribe(StationSignalKind.StationSignalRawIqCf32, token);
        float[] data = Enumerable.Range(0, 2048).Select(i => (i - 1024) / 1024f).ToArray();
        byte[] bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
        tap.Publish(data, 2000000);
        Require(await stream.ResponseStream.MoveNext(token), "No native-rate raw IQ frame.");
        var first = stream.ResponseStream.Current;
        Require(first.SampleRate == 2000000 && first.Channels == 2 && first.SampleCount == 1024 &&
            first.FirstSampleIndex == 0 && first.Payload.Span.SequenceEqual(bytes), "Raw IQ stream changed sample rate or CF32 payload.");
        tap.Publish(data, 2000000);
        Require(await stream.ResponseStream.MoveNext(token) && stream.ResponseStream.Current.FirstSampleIndex == 1024,
            "Raw IQ stream lost sample index.");
        tap.Reset();
        tap.Publish(data, 2000000);
        Require(await stream.ResponseStream.MoveNext(token) && stream.ResponseStream.Current.Epoch != first.Epoch &&
            stream.ResponseStream.Current.FirstSampleIndex == 0, "Raw IQ hardware overflow did not reset epoch.");
        Console.WriteLine("PASS: raw SDR tap through real Station IPC retains native 2 MS/s CF32 and overflow epoch.");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
