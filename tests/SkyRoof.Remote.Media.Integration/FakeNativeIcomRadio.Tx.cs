using System.Buffers.Binary;
using SkyRoof.Remote.Foundation.Media;

internal sealed partial class FakeNativeIcomRadio
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<short, byte> _txSamples = new();

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
                    _txSamples.TryAdd(BinaryPrimitives.ReadInt16LittleEndian(frame.Pcm16.AsSpan(i, 2)), 0);
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
