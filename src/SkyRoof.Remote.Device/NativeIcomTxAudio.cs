using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Device;

// Access is serialized by StationDeviceService's command gate, including
// each UDP send tick. An epoch change discards the previous owner's PCM.
internal sealed class NativeIcomTxAudio(IcomLanAudioSocket socket, int rate, IcomLanAudioCodec codec)
{
    private readonly IcomLanTxPacer _pacer = new(socket, rate, codec);
    private readonly IcomLanAudioResampler _resampler = new(rate);
    private readonly TxPcmReorder _reorder = new();
    private ulong _epoch;
    private bool _started;

    public void Select(ulong epoch)
    {
        if (epoch == _epoch) return;
        _epoch = epoch;
        _reorder.Reset();
        _started = false;
        _resampler.Reset();
        _pacer.Reset();
    }

    public bool Push(ulong sequence, ReadOnlySpan<byte> pcm)
    {
        if (!_reorder.Push(sequence, pcm)) return false;
        _started = true;
        return true;
    }

    public async Task TickAsync(CancellationToken ct)
    {
        if (!_started) return;
        _reorder.Drain(block => _pacer.Push(_resampler.Process(block, 48000)));
        await _pacer.SendTickAsync(ct);
    }
}
