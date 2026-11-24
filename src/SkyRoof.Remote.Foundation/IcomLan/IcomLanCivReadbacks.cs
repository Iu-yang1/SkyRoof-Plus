namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed class IcomLanCivReadbacks(IcomLanCivSocket socket, byte radioAddress)
{
    public async Task<long> ReadSelectedFrequencyAsync(CancellationToken ct = default)
    {
        byte[] data = await socket.QueryAsync(new(radioAddress, 0xE0, [0x03], 5), ct);
        if (!global::SkyRoof.IcomCivFrequencyCodec.TryDecode(data, out long hz))
            throw new InvalidDataException("CI-V frequency response contains invalid packed BCD.");
        return hz;
    }

    public async Task<IcomCivMode> ReadSelectedModeAsync(CancellationToken ct = default)
    {
        byte[] data = await socket.QueryAsync(new(radioAddress, 0xE0, [0x04], 2), ct);
        return new IcomCivMode(data[0], data[1]);
    }

    public async Task<(bool Enabled, byte Filter)> ReadDataModeAsync(CancellationToken ct = default)
    {
        byte[] data = await socket.QueryAsync(new(radioAddress, 0xE0, [0x1A, 6], 2), ct);
        if (data[0] > 1 || (data[0] == 0 ? data[1] != 0 : data[1] is < 1 or > 3))
            throw new InvalidDataException("Invalid CI-V data mode readback.");
        return (data[0] == 1, data[1]);
    }

    public async Task<bool> ReadPttAsync(CancellationToken ct = default)
    {
        byte[] data = await socket.QueryAsync(new(radioAddress, 0xE0, [0x1C, 0], 1), ct);
        return data[0] switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("CI-V PTT response is neither RX nor TX.")
        };
    }
}
