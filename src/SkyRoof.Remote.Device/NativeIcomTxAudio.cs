using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Device;

// Access is serialized by StationDeviceService's command gate, including
// each UDP send tick. An epoch change discards the previous owner's PCM.
internal sealed class NativeIcomTxAudio(IcomLanAudioSocket socket, int rate, IcomLanAudioCodec codec)
{
    private readonly IcomLanTxPacer _pacer = new(socket, rate, codec);
    private readonly IcomLanAudioResampler _resampler = new(rate);
    private ulong _epoch, _nextSequence;
    private bool _started;

    public void Select(ulong epoch)
    {
        if (epoch == _epoch) return;
        _epoch = epoch;
        _nextSequence = 0;
        _started = false;
        _resampler.Reset();
        _pacer.Reset();
    }

    public bool Push(ulong sequence, ReadOnlySpan<byte> pcm)
    {
        if (_started && sequence < _nextSequence) return false;
        if (_started && sequence != _nextSequence)
        {
            _resampler.Reset();
            _pacer.Reset();
        }
        _nextSequence = sequence + 1;
        _started = true;
        _pacer.Push(_resampler.Process(pcm, 48000));
        return true;
    }

    public Task TickAsync(CancellationToken ct) =>
        _started ? _pacer.SendTickAsync(ct) : Task.CompletedTask;
}
