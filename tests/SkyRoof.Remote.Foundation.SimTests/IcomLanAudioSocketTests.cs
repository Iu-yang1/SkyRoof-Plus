using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;

internal static class IcomLanAudioSocketTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        using var fake = new FakeIcomLanAudioRadio();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var fakeTask = fake.RunAsync(cancellation.Token);
        await using (var socket = await IcomLanAudioSocket.OpenAsync(
            "127.0.0.1", fake.Port, cancellation.Token))
        {
            if (socket.RemoteSid != FakeIcomLanAudioRadio.RadioSid ||
                socket.LocalSid == 0)
                throw new InvalidOperationException("Icom audio UDP session SID handshake failed.");
            byte[] audio = await socket.ReadAudioFrameAsync(
                IcomLanAudioCodec.Lpcm16Mono, cancellation.Token);
            if (!IcomLanAudioPacketCodec.TryDecode(audio, socket.RemoteSid,
                socket.LocalSid, IcomLanAudioCodec.Lpcm16Mono, out var decoded) ||
                decoded.OuterSequence != 42 || decoded.AudioSequence != 9 ||
                !decoded.Pcm16.All(value => value == 0x5A))
                throw new InvalidOperationException("Direct UDP audio framing or SID check failed.");
            var disconnected = false;
            try
            {
                _ = await socket.ReadAudioFrameAsync(
                    IcomLanAudioCodec.Lpcm16Mono, cancellation.Token);
            }
            catch (IOException) { disconnected = true; }
            if (!disconnected)
                throw new InvalidOperationException("Radio logout was not propagated to audio owner.");
        }
        await fakeTask;
        Console.WriteLine("PASS: authenticated-grant audio UDP handshake, ping clock, radio ping echo, PCM and disconnect.");
    }
}
