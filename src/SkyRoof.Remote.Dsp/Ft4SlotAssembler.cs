using System.Buffers.Binary;
using SkyRoof.Remote.SignalBus;
using VE3NEA;

namespace SkyRoof.Remote.Dsp;

internal sealed record Ft4SlotWindow(long SlotUtcTicks, ulong SourceEpoch, float[] Samples);

// Sample-domain UTC alignment. The input anchor denotes the first sample,
// not the arrival of each packet. A gap discards the interrupted window.
internal sealed class Ft4SlotAssembler
{
    private const int Rate = 48000;
    private const long SlotTicks = TimeSpan.TicksPerSecond * 15 / 2;
    private const long SlotSamples = 360000;
    private ulong _epoch, _next;
    private long _anchorSample, _slot;
    private ulong _anchorIndex;
    private float[]? _window;
    private int _filled;

    public void Accept(SignalFrame frame, int channels, DateTime firstSampleUtc,
        Action<Ft4SlotWindow> completed)
    {
        if (frame.SampleRate != Rate || channels is < 1 or > 2 ||
            frame.Payload.Length != frame.SampleCount * channels * 2)
            throw new InvalidDataException("Invalid FT4 PCM geometry.");
        if (_epoch != frame.StreamEpoch || frame.FirstSampleIndex != _next)
        {
            if (_epoch != frame.StreamEpoch)
            {
                _anchorIndex = frame.FirstSampleIndex;
                _anchorSample = ToSample(firstSampleUtc.Ticks);
            }
            _epoch = frame.StreamEpoch;
            _window = null;
            _filled = 0;
        }
        long start = _anchorSample + checked((long)(frame.FirstSampleIndex - _anchorIndex));
        for (int i = 0; i < frame.SampleCount; i++) Add(start + i,
            ReadMono(frame.Payload, i, channels), completed);
        _next = frame.FirstSampleIndex + (ulong)frame.SampleCount;
    }

    private void Add(long position, float sample, Action<Ft4SlotWindow> completed)
    {
        long slot = Math.DivRem(position, SlotSamples, out long offset);
        if (offset == 0)
        {
            _slot = slot;
            _window = new float[NativeFT4Coder.DECODE_SAMPLE_COUNT];
            _filled = 0;
        }
        if (_window is null || slot != _slot || offset != _filled) return;
        _window[_filled++] = sample;
        if (_filled == _window.Length)
        {
            completed(new(_slot * SlotTicks, _epoch, _window));
            _window = null;
        }
    }

    private static long ToSample(long ticks) =>
        ticks / TimeSpan.TicksPerSecond * Rate +
        (long)Math.Round((ticks % TimeSpan.TicksPerSecond) * (double)Rate / TimeSpan.TicksPerSecond);

    private static float ReadMono(byte[] bytes, int index, int channels)
    {
        int offset = index * channels * 2;
        int sum = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));
        if (channels == 2) sum += BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 2, 2));
        return sum / (32768f * channels);
    }
}
