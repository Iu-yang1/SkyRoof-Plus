namespace SkyRoof.Remote.Foundation.Media;

public readonly record struct IcomLanOrderedRxPacket(
    ushort OuterSequence, ulong FirstSampleIndex, int SampleRate, byte[] Pcm16);

public readonly record struct IcomLanReorderDiagnostics(
    ulong Received, ulong OutOfOrder, ulong Dropped, ulong Missing);

/// <summary>
/// Reorders Icom LAN audio by its outer UDP sequence, rather than assuming
/// the inner audio sequence advances on every packet. Late repeats are ignored.
/// </summary>
public sealed class IcomLanPacketReorderBuffer
{
    private const int MaxPendingFrames = 8;
    private const int MaxForwardJump = 256;
    private static readonly TimeSpan ReorderDeadline = TimeSpan.FromMilliseconds(100);
    private readonly Dictionary<ushort, IcomLanOrderedRxPacket> _pending = [];
    private ushort? _nextSequence;
    private DateTime _missingSinceUtc;
    private ulong _received;
    private ulong _outOfOrder;
    private ulong _dropped;
    private ulong _missing;

    public IcomLanReorderDiagnostics Diagnostics =>
        new(_received, _outOfOrder, _dropped, _missing);

    public IReadOnlyList<IcomLanOrderedRxPacket> Push(
        IcomLanOrderedRxPacket packet, DateTime utc)
    {
        _received++;
        _nextSequence ??= packet.OuterSequence;
        int ahead = (ushort)(packet.OuterSequence - _nextSequence.Value);
        if (ahead >= 0x8000)
        {
            _dropped++;
            return Array.Empty<IcomLanOrderedRxPacket>();
        }
        if (ahead > 0) _outOfOrder++;
        if (ahead > MaxForwardJump)
        {
            _missing += (ulong)ahead;
            _pending.Clear();
            _nextSequence = packet.OuterSequence;
            _missingSinceUtc = default;
        }
        if (!_pending.TryAdd(packet.OuterSequence, packet))
        {
            _dropped++;
            return Array.Empty<IcomLanOrderedRxPacket>();
        }
        return Drain(utc, force: false);
    }

    public IReadOnlyList<IcomLanOrderedRxPacket> Flush(DateTime utc,
        bool force = false) => Drain(utc, force);

    private IReadOnlyList<IcomLanOrderedRxPacket> Drain(DateTime utc, bool force)
    {
        var ready = new List<IcomLanOrderedRxPacket>();
        while (_nextSequence is ushort expected)
        {
            if (_pending.Remove(expected, out var packet))
            {
                ready.Add(packet);
                _nextSequence = unchecked((ushort)(expected + 1));
                _missingSinceUtc = default;
                continue;
            }
            if (_pending.Count == 0) break;
            if (_missingSinceUtc == default)
                _missingSinceUtc = utc;
            if (!force && _pending.Count < MaxPendingFrames &&
                utc - _missingSinceUtc < ReorderDeadline)
                break;
            _nextSequence = _pending.Keys.MinBy(
                sequence => (ushort)(sequence - expected));
            _missing += (ushort)(_nextSequence.Value - expected);
            _missingSinceUtc = default;
        }
        return ready;
    }
}
