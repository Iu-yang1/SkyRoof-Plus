using System.Buffers.Binary;
using SkyRoof.Remote.Foundation.Signals;

namespace SkyRoof.Remote.WsjtxBridge;

public sealed record RxPcmBlock(bool ResetBuffer, byte[] Pcm);

public sealed class RxPcmTimeline
{
    private const ulong MaxGapSamples = 4800;
    private bool _initialized;
    private uint _epoch;
    private ulong _next;

    public RxPcmBlock? Accept(ReadOnlySpan<byte> packet)
    {
        if (!AudioFrameHeader.TryRead(packet, out var header) || header.Codec != 2 || header.SampleRate != 48000)
            throw new InvalidDataException("Invalid SAP/1 RX PCM packet.");
        int samples = header.SamplesPerChannel;
        int channels = header.Channels;
        if (samples == 0 || channels is < 1 or > 2 ||
            packet.Length != AudioFrameHeader.Size + samples * channels * 2 ||
            header.PayloadLength != samples * channels * 2 ||
            header.FirstSampleIndex > ulong.MaxValue - (ulong)samples)
            throw new InvalidDataException("Invalid SAP/1 sample index or payload size.");

        ulong first = header.FirstSampleIndex;
        ulong end = first + (ulong)samples;
        bool reset = !_initialized || _epoch != header.StreamEpoch;
        if (!reset && end <= _next) return null;
        ulong gap = !reset && first > _next ? first - _next : 0;
        if (gap > MaxGapSamples) reset = true;
        int skip = !reset && first < _next ? (int)(_next - first) : 0;
        int silence = reset ? 0 : (int)gap;
        byte[] pcm = new byte[(silence + samples - skip) * 2];
        CopyMono(packet[AudioFrameHeader.Size..], pcm.AsSpan(silence * 2), channels, skip);
        _initialized = true;
        _epoch = header.StreamEpoch;
        _next = end;
        return new RxPcmBlock(reset, pcm);
    }

    private static void CopyMono(ReadOnlySpan<byte> source, Span<byte> target, int channels, int skip)
    {
        for (int i = 0; i < target.Length / 2; i++)
        {
            int offset = (skip + i) * channels * 2;
            int value = BinaryPrimitives.ReadInt16LittleEndian(source[offset..]);
            if (channels == 2)
                value = (value + BinaryPrimitives.ReadInt16LittleEndian(source[(offset + 2)..])) / 2;
            BinaryPrimitives.WriteInt16LittleEndian(target[(i * 2)..], (short)value);
        }
    }
}
