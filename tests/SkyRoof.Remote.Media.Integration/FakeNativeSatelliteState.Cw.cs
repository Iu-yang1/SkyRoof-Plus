using System.Text;

internal sealed partial class FakeNativeSatelliteState
{
    private byte[] _cwSpeed = [0, 0x85];
    private int _cwStops;
    private string _lastCw = "";
    public void SeedOrphanedCw() { _ptt = 1; _mode[1] = [3, 1]; }
    public int CwStops => Volatile.Read(ref _cwStops);
    public string LastCw => Volatile.Read(ref _lastCw);

    private byte[] SetCwSpeed(byte[] bcd)
    {
        if (_selected != 1) throw new InvalidDataException("CW WPM not applied to SAT SUB.");
        if (!bcd.AsSpan().SequenceEqual(new byte[] { 0x01, 0x15 }))
            throw new InvalidDataException("Expected packed BCD 0115 for the 25 WPM fixture.");
        _cwSpeed = bcd;
        return [0xFB];
    }

    private byte[] SendCw(byte[] ascii)
    {
        if (_selected != 1 || _mode[1][0] is not (3 or 7) || _ptt != 0)
            return [0xFA];
        Volatile.Write(ref _lastCw, Encoding.ASCII.GetString(ascii));
        _ptt = 1;
        return [0xFB];
    }

    private byte[] StopCw()
    {
        if (_selected != 1) throw new InvalidDataException("CW STOP sent to wrong band.");
        _ptt = 0;
        Interlocked.Increment(ref _cwStops);
        return [0xFB];
    }
}
