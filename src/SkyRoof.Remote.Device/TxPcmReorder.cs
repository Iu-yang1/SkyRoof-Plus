namespace SkyRoof.Remote.Device;

// At most 240 ms of incoming 20-ms frames. Wait two pacing ticks for a
// missing frame before inserting silence; late data never extends latency.
internal sealed class TxPcmReorder
{
    private const int Window = 12;
    private static readonly byte[] Silence = new byte[1920];
    private readonly SortedDictionary<ulong, byte[]> _pending = new();
    private ulong _next;
    private int _missingTicks;

    public void Reset()
    {
        _pending.Clear();
        _next = 0;
        _missingTicks = 0;
    }

    public bool Push(ulong sequence, ReadOnlySpan<byte> pcm)
    {
        if (sequence < _next || _pending.ContainsKey(sequence)) return false;
        if (sequence - _next >= Window)
        {
            _pending.Clear();
            _next = sequence;
            _missingTicks = 0;
        }
        _pending.Add(sequence, pcm.ToArray());
        return true;
    }

    public void Drain(Action<byte[]> consume)
    {
        if (_pending.Count == 0) return;
        if (!_pending.ContainsKey(_next))
        {
            if (++_missingTicks < 2) return;
            // Fill only the gap up to the next buffered packet.
            ulong first = _pending.First().Key;
            while (_next < first) { consume(Silence); _next++; }
        }
        while (_pending.Remove(_next, out byte[]? data))
        {
            consume(data);
            _next++;
        }
        _missingTicks = 0;
    }
}
