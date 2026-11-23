namespace SkyRoof.Remote.Foundation.IcomLan;

public readonly record struct IcomLanTimedRxFrame(
    ushort OuterSequence, ulong FirstSampleIndex, byte[] Pcm16);

/// <summary>
/// Source-side audio playout clock. Assign indices only when the UDP packet
/// ordering is settled; arrival order is never treated as the sample clock.
/// </summary>
public sealed partial class IcomLanRxFrameClock
{
    private readonly Dictionary<ushort, byte[]> _pending = [];
    private ushort? _nextSequence;
    private ulong _nextSample;
    private int _lastFrameSamples;
    private DateTime _gapSince;
    private const int MaxBufferedPackets = 8;
    private const int MaxForwardJump = 256;
    private static readonly TimeSpan ReorderTimeout = TimeSpan.FromMilliseconds(100);

    public IReadOnlyList<IcomLanTimedRxFrame> Push(
        ushort outerSequence, ReadOnlySpan<byte> pcm16, DateTime utc)
    {
        if (pcm16.Length == 0 || (pcm16.Length & 1) != 0)
            throw new ArgumentException("Icom RX requires aligned PCM16.");
        _nextSequence ??= outerSequence;
        int ahead = (ushort)(outerSequence - _nextSequence.Value);
        if (ahead >= 0x8000) return Array.Empty<IcomLanTimedRxFrame>();
        if (ahead > MaxForwardJump)
        {
            _pending.Clear();
            SkipSequenceGap(ahead);
            _nextSequence = outerSequence;
            _gapSince = default;
        }
        if (!_pending.TryAdd(outerSequence, pcm16.ToArray()))
            return Array.Empty<IcomLanTimedRxFrame>();
        return Drain(utc, force: false);
    }

    public IReadOnlyList<IcomLanTimedRxFrame> Flush(DateTime utc, bool force = false) =>
        Drain(utc, force);

    private void SkipSequenceGap(int sequenceGap)
    {
        if (_lastFrameSamples != 0)
            _nextSample = checked(_nextSample +
                checked((ulong)sequenceGap * (ulong)_lastFrameSamples));
    }
}
