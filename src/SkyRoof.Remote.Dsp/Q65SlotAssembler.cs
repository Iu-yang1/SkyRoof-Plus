using System.Buffers.Binary;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

internal sealed record Q65Slot(long Index, ulong Epoch, short[] Samples);

internal sealed class Q65SlotAssembler(int seconds)
{
    private readonly long _slotSamples = seconds * 48000L;
    private ulong _epoch, _anchorIndex, _next;
    private long _anchorSample;
    private short[]? _samples;
    private int _filled;

    public bool IsDiscontinuous(SignalFrame frame) =>
        _epoch != frame.StreamEpoch || _next != frame.FirstSampleIndex;

    public void Accept(SignalFrame frame, int channels, DateTimeOffset firstUtc, Action<Q65Slot> completed)
    {
        if (frame.SampleRate != 48000 || frame.SampleCount <= 0 || channels is < 1 or > 2 ||
            frame.Payload.Length != frame.SampleCount * channels * 2)
            throw new InvalidDataException("Invalid Q65 PCM geometry.");
        if (IsDiscontinuous(frame))
        {
            _samples = null;
            _filled = 0;
            if (_epoch != frame.StreamEpoch || frame.FirstSampleIndex < _anchorIndex)
            {
                _anchorIndex = frame.FirstSampleIndex;
                long ticks = firstUtc.UtcTicks - DateTime.UnixEpoch.Ticks;
                _anchorSample = ticks / TimeSpan.TicksPerSecond * 48000 +
                    (long)Math.Round((ticks % TimeSpan.TicksPerSecond) * 48000.0 / TimeSpan.TicksPerSecond);
            }
            _epoch = frame.StreamEpoch;
        }
        long start = _anchorSample + checked((long)(frame.FirstSampleIndex - _anchorIndex));
        for (int i = 0; i < frame.SampleCount; i++)
        {
            long index = Math.DivRem(start + i, _slotSamples, out long offset);
            if (offset == 0) { _samples = new short[(int)_slotSamples]; _filled = 0; }
            if (_samples is null || offset != _filled) continue;
            int at = i * channels * 2;
            int value = BinaryPrimitives.ReadInt16LittleEndian(frame.Payload.AsSpan(at, 2));
            if (channels == 2) value = (value + BinaryPrimitives.ReadInt16LittleEndian(frame.Payload.AsSpan(at + 2, 2))) / 2;
            _samples[_filled++] = (short)value;
            if (_filled == _samples.Length)
            {
                completed(new(index, _epoch, _samples));
                _samples = null;
            }
        }
        _next = frame.FirstSampleIndex + (ulong)frame.SampleCount;
    }
}
