namespace SkyRoof.Remote.Foundation.IcomLan;

// Small replay window retains out-of-order UDP replies and handles wrap.
internal sealed class IcomLanCivSequenceWindow
{
    private ushort? _latest;
    private ulong _seen;

    public bool Accept(ushort sequence)
    {
        if (_latest is not ushort latest)
        {
            _latest = sequence;
            _seen = 1;
            return true;
        }
        int forward = (ushort)(sequence - latest);
        if (forward == 0) return false;
        if (forward < 32768)
        {
            _seen = forward >= 64 ? 1UL : (_seen << forward) | 1;
            _latest = sequence;
            return true;
        }
        int age = (ushort)(latest - sequence);
        if (age >= 64 || (_seen & (1UL << age)) != 0) return false;
        _seen |= 1UL << age;
        return true;
    }
}
