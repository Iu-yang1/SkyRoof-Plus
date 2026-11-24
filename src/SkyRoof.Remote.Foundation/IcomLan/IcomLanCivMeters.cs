namespace SkyRoof.Remote.Foundation.IcomLan;

// Raw CI-V meter values are not calibrated dBm or watts.
public enum IcomCivMeter : byte
{
    Signal = 0x02, Power = 0x11, Swr = 0x12, Alc = 0x13
}

public sealed class IcomLanCivMeters(IcomLanCivSocket socket, byte radioAddress)
{
    public async Task<int> ReadAsync(IcomCivMeter meter, CancellationToken ct = default)
    {
        byte[] data = await socket.QueryAsync(new(radioAddress, 0xE0, [0x15, (byte)meter], 2), ct);
        int value = 0;
        foreach (byte digitPair in data)
        {
            int high = digitPair >> 4, low = digitPair & 15;
            if (high > 9 || low > 9)
                throw new InvalidDataException("Invalid CI-V meter BCD.");
            value = value * 100 + high * 10 + low;
        }
        if (value > 255) throw new InvalidDataException("CI-V meter outside 0–255.");
        return value;
    }
}
