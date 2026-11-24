namespace SkyRoof.Remote.Foundation.IcomLan;

// Only the single native CI-V receive loop calls Observe. Readers sample
// immutable latest complete sweeps, independently of CAT readback latency.
public sealed class IcomLanRfScopeReceiver(byte radioAddress)
{
    private readonly global::SkyRoof.IcomScopeAssembler _assembler = new();
    private readonly IcomLanRfScopeFrame?[] _latest = new IcomLanRfScopeFrame?[2];

    public IcomLanRfScopeFrame? Latest(bool sub) => Volatile.Read(ref _latest[sub ? 1 : 0]);

    public void Observe(ReadOnlySpan<byte> payload)
    {
        for (int i = 0; i + 7 < payload.Length; i++)
        {
            if (payload[i] != 0xFE || payload[i + 1] != 0xFE ||
                payload[i + 2] is not (0x00 or 0xE0) || payload[i + 3] != radioAddress ||
                payload[i + 4] != 0x27 || payload[i + 5] != 0)
                continue;
            int end = payload[(i + 6)..].IndexOf((byte)0xFD);
            if (end < 0) break;
            int length = end + 7;
            Accept(payload.Slice(i, length).ToArray());
            i += length - 1;
        }
    }

    private void Accept(byte[] frame)
    {
        if (!_assembler.TryFeed(frame, out var sweep) ||
            sweep is null || !sweep.SweepComplete) return;
        var geometry = sweep.Geometry;
        if (!geometry.IsValid) return;
        var result = new IcomLanRfScopeFrame(sweep.Scope, sweep.Mode, sweep.OutOfRange,
            geometry.LowerFrequencyHz, geometry.UpperFrequencyHz, sweep.Samples);
        Volatile.Write(ref _latest[sweep.Scope], result);
    }
}
