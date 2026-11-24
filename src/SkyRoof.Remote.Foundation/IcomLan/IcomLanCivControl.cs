namespace SkyRoof.Remote.Foundation.IcomLan;

/// <summary>IC-9700 selected receiver commands. The caller owns any multi-command band-selection transaction.</summary>
public sealed class IcomLanCivControl(IcomLanCivSocket socket, byte radioAddress)
{
    private readonly IcomLanCivReadbacks _readbacks = new(socket, radioAddress);

    public async Task SetSelectedFrequencyAsync(long hz, CancellationToken ct = default)
    {
        if (hz <= 0 || !global::SkyRoof.IcomCivFrequencyCodec.TryEncode(hz, 5, out var bcd))
            throw new ArgumentOutOfRangeException(nameof(hz));
        await WriteAsync([0x05, .. bcd], ct);
        if (await _readbacks.ReadSelectedFrequencyAsync(ct) != hz)
            throw new IOException("IC-9700 frequency readback differs from the requested frequency.");
    }

    public async Task SetSelectedModeAsync(IcomCivMode mode, CancellationToken ct = default)
    {
        if (mode.Filter is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(mode));
        await WriteAsync([0x06, mode.Code, mode.Filter], ct);
        if (await _readbacks.ReadSelectedModeAsync(ct) != mode)
            throw new IOException("IC-9700 mode/filter readback differs from the requested setting.");
    }

    // 07 D0/D1 select receiver focus; these are not VFO A/B commands.
    public Task SelectBandAsync(bool sub, CancellationToken ct = default) =>
        WriteAsync([0x07, sub ? (byte)0xD1 : (byte)0xD0], ct);

    public async Task SetPttAsync(bool on, CancellationToken ct = default)
    {
        await WriteAsync([0x1C, 0, on ? (byte)1 : (byte)0], ct);
        if (await _readbacks.ReadPttAsync(ct) != on)
            throw new IOException("IC-9700 PTT readback differs from the requested state.");
    }

    public async Task WriteAsync(byte[] command, CancellationToken ct = default)
    {
        var request = new IcomCivQuery(radioAddress, 0xE0, command, 0)
            { ExpectsAcknowledgement = true };
        byte[] reply = await socket.QueryAsync(request, ct);
        if (reply[0] != 0xFB)
            throw new IOException("IC-9700 rejected the CI-V command (FA).");
    }
}
