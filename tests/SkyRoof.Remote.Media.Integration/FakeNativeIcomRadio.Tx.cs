using System.Buffers.Binary;
using SkyRoof.Remote.Foundation.Media;

internal sealed partial class FakeNativeIcomRadio
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<short, byte> _txSamples = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<short> _timedSamples = new();
    public short[] TimedSamples => _timedSamples.ToArray();
    private System.Collections.Concurrent.ConcurrentQueue<short>? _capture;
    public void BeginWaveformCapture() => Volatile.Write(ref _capture, new());
    public short[] EndWaveformCapture() => Interlocked.Exchange(ref _capture, null)?.ToArray() ?? [];

    private async Task ObserveTxAsync(uint local, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _nativeCatDone.Token);
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var packet = await _audio.ReceiveAsync(lifetime.Token);
                if (!IcomLanAudioPacketCodec.TryDecode(packet.Buffer, local, AudioSid,
                    IcomLanAudioCodec.Lpcm16Mono, out var frame)) continue;
                for (int i = 0; i < frame.Pcm16.Length; i += 2)
                {
                    short sample = BinaryPrimitives.ReadInt16LittleEndian(frame.Pcm16.AsSpan(i, 2));
                    _txSamples.TryAdd(sample, 0);
                    Volatile.Read(ref _capture)?.Enqueue(sample);
                    if (sample is >= 6000 and < 6960) _timedSamples.Enqueue(sample);
                }
            }
        }
        catch (OperationCanceledException) when (_nativeCatDone.IsCancellationRequested)
        { Console.WriteLine("PASS: fake radio TX UDP collector completed."); }
    }

    public async Task WaitForTxSampleAsync(short sample, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!_txSamples.ContainsKey(sample)) await Task.Delay(20, timeout.Token);
    }
}
