using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Foundation.IcomLan;

/// <summary>Original SkyRoof 20-ms pacer, bound to a negotiated TX format.</summary>
public sealed class IcomLanTxPacer(IcomLanAudioSocket socket, int sampleRate,
    IcomLanAudioCodec codec)
{
    private readonly global::SkyRoof.IcomLanTxAudioPacerBuffer _buffer = new(sampleRate, 60, 250);

    public void Reset() => _buffer.Reset();
    public void Push(ReadOnlySpan<float> samples) => _buffer.Push(samples);

    public async Task SendTickAsync(CancellationToken ct)
    {
        float[] frame = _buffer.NextFrame();
        var pcm = new short[frame.Length];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (short)Math.Clamp(Math.Round(frame[i] * 32768.0), -32768, 32767);
        await socket.SendPcmAsync(pcm, codec, ct);
    }

    // The caller owns the input and RF transaction lifetime. The pacer stops
    // emitting immediately when its cancellation token is cancelled.
    public async Task RunAsync(System.Threading.Channels.ChannelReader<float[]> input,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (await timer.WaitForNextTickAsync(ct))
        {
            while (input.TryRead(out var block)) _buffer.Push(block);
            await SendTickAsync(ct);
        }
    }
}
