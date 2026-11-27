using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Device;

// A sealed waveform does not use the microphone jitter buffer: dropping or
// reordering any of its samples would corrupt the digital modulation.
internal sealed class TimedNativeWaveform
{
    private readonly IcomLanAudioSocket _socket;
    private readonly IcomLanAudioCodec _codec;
    private readonly int _rate;
    private readonly short[] _samples;
    private int _offset, _remainder;

    public TimedNativeWaveform(IcomLanAudioSocket socket, int rate,
        IcomLanAudioCodec codec, byte[] pcm)
    {
        _socket = socket;
        _rate = rate;
        _codec = codec;
        var resampler = new IcomLanAudioResampler(rate);
        int count = checked((int)Math.Round(pcm.Length / 2.0 * rate / 48000));
        // Complete the sinc tail using silence, retaining the requested duration.
        var padded = new byte[pcm.Length + 48];
        pcm.CopyTo(padded, 0);
        var converted = resampler.Process(padded, 48000);
        if (converted.Length < count) throw new InvalidDataException("Incomplete TX waveform conversion.");
        _samples = Array.ConvertAll(converted[..count], value =>
            (short)Math.Clamp(Math.Round(value * 32768.0), -32768, 32767));
    }

    public bool Completed => _offset >= _samples.Length;

    public async Task SendNextAsync(CancellationToken ct)
    {
        int count = _rate / 50;
        _remainder += _rate % 50;
        if (_remainder >= 50) { count++; _remainder -= 50; }
        var frame = new short[count];
        int remaining = Math.Min(count, _samples.Length - _offset);
        _samples.AsSpan(_offset, remaining).CopyTo(frame);
        await _socket.SendPcmAsync(frame, _codec, ct);
        _offset += remaining;
    }
}
