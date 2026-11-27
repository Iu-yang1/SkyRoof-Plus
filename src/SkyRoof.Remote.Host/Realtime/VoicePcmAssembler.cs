using System.Buffers.Binary;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

// Opus consumes exactly 960 mono samples at 48 kHz. Preserve complete frames
// across arbitrary SignalBus packet sizes, never join different RX epochs.
internal sealed class VoicePcmAssembler(int channels)
{
    private readonly short[] _frame = new short[960];
    private int _filled;
    private ulong _epoch, _next;

    public void Accept(SignalFrame source, Action<short[]> send)
    {
        if (source.SampleRate != 48000 || source.Payload.Length != source.SampleCount * channels * 2)
            throw new InvalidDataException("Invalid voice PCM source.");
        if (_epoch != source.StreamEpoch || _next != source.FirstSampleIndex)
        {
            _filled = 0;
            _epoch = source.StreamEpoch;
        }
        for (int i = 0; i < source.SampleCount; i++)
        {
            int offset = i * channels * 2;
            int sample = BinaryPrimitives.ReadInt16LittleEndian(source.Payload.AsSpan(offset, 2));
            if (channels == 2)
                sample = (sample + BinaryPrimitives.ReadInt16LittleEndian(source.Payload.AsSpan(offset + 2, 2))) / 2;
            _frame[_filled++] = (short)sample;
            if (_filled == _frame.Length)
            {
                send(_frame);
                _filled = 0;
            }
        }
        _next = source.FirstSampleIndex + (ulong)source.SampleCount;
    }
}
