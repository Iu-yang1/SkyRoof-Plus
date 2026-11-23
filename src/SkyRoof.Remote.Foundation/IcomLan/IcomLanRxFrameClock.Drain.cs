namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanRxFrameClock
{
    private IReadOnlyList<IcomLanTimedRxFrame> Drain(DateTime utc, bool force)
    {
        var ready = new List<IcomLanTimedRxFrame>();
        while (_nextSequence is ushort expected)
        {
            if (_pending.Remove(expected, out var pcm))
            {
                ready.Add(new IcomLanTimedRxFrame(expected, _nextSample, pcm));
                _nextSample = checked(_nextSample + (ulong)(pcm.Length / 2));
                _lastFrameSamples = pcm.Length / 2;
                _nextSequence = unchecked((ushort)(expected + 1));
                _gapSince = default;
                continue;
            }
            if (_pending.Count == 0) break;
            if (_gapSince == default) _gapSince = utc;
            if (!force && _pending.Count < 8 &&
                utc - _gapSince < TimeSpan.FromMilliseconds(100))
                break;
            ushort next = _pending.Keys.MinBy(key => (ushort)(key - expected));
            SkipSequenceGap((ushort)(next - expected));
            _nextSequence = next;
            _gapSince = default;
        }
        return ready;
    }
}
