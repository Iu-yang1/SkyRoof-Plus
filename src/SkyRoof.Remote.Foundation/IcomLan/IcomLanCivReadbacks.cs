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
