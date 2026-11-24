internal sealed class FakeNativeSatelliteState
{
    private readonly byte[][] _frequency = [[0, 0, 0x90, 0x45, 1], [0, 0, 0x65, 0x35, 4]];
    private readonly byte[][] _mode = [[1, 1], [0, 1]];
    private readonly byte[][] _data = [[0, 0], [0, 0]];
    private int _selected;
    private byte _ptt;

    public byte[] Apply(byte[] command)
    {
        return command switch
        {
            [0x16, 0x5A] => [0x16, 0x5A, 1],
            [7, 0xD0] => Select(0),
            [7, 0xD1] => Select(1),
            [3] => [3, .. _frequency[_selected]],
            [5, _, _, _, _, _] => Set(_frequency, command[1..]),
            [4] => [4, .. _mode[_selected]],
            [6, _, _] => Set(_mode, command[1..]),
            [0x1A, 6] => [0x1A, 6, .. _data[_selected]],
            [0x1A, 6, _, _] => Set(_data, command[2..]),
            [0x1C, 0] => [0x1C, 0, _ptt],
            [0x1C, 0, 0] => SetPtt(0),
            [0x1C, 0, 1] => SetPtt(1),
            _ => throw new InvalidDataException("Unexpected fake satellite CAT command: " +
                Convert.ToHexString(command))
        };
    }

    private byte[] Select(int band)
    {
        _selected = band;
        return [0xFB];
    }

    private byte[] Set(byte[][] values, byte[] data)
    {
        values[_selected] = data;
        return [0xFB];
    }

    private byte[] SetPtt(byte value)
    {
        if (_selected != 0)
            throw new InvalidOperationException("Native CAT left SUB selected when keying.");
        _ptt = value;
        return [0xFB];
    }
}
