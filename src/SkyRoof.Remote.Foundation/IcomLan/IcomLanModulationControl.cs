namespace SkyRoof.Remote.Foundation.IcomLan;

public enum IcomLanModulationInput : byte
{
    Mic = 0, Acc = 1, MicAcc = 2, Usb = 3, MicUsb = 4, Lan = 5
}

// Explicit register control. A receive-only login must not silently change
// the radio's front-panel microphone source.
public sealed class IcomLanModulationControl(IcomLanCivSocket socket, byte radioAddress)
{
    private readonly IcomLanCivControl _control = new(socket, radioAddress);

    public async Task<IcomLanModulationInput> ReadAsync(bool dataMode, CancellationToken ct)
    {
        byte[] value = await socket.QueryAsync(new(radioAddress, 0xE0,
            [0x1A, 5, 1, dataMode ? (byte)0x16 : (byte)0x15], 1), ct);
        if (value[0] > 5) throw new InvalidDataException("Invalid IC-9700 MOD input.");
        return (IcomLanModulationInput)value[0];
    }

    public async Task SetAsync(bool dataMode, IcomLanModulationInput input, CancellationToken ct)
    {
        if (!Enum.IsDefined(input)) throw new ArgumentOutOfRangeException(nameof(input));
        await _control.WriteAsync([0x1A, 5, 1, dataMode ? (byte)0x16 : (byte)0x15, (byte)input], ct);
        if (await ReadAsync(dataMode, ct) != input)
            throw new IOException("IC-9700 MOD input readback differs.");
    }
}
