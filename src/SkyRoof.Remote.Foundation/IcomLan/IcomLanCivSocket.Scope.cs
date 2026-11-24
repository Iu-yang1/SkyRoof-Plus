namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanCivSocket
{
    private IcomLanRfScopeReceiver? _rfScope;
    public IcomLanRfScopeReceiver? RfScope => Volatile.Read(ref _rfScope);

    public async Task EnableRfScopeAsync(IcomLanRadioCapabilities? capabilities,
        CancellationToken ct)
    {
        if (capabilities is not { RadioName: "IC-9700" } radio)
            throw new InvalidOperationException("Native RF scope currently uses the authenticated IC-9700 profile.");
        var control = new IcomLanCivControl(this, radio.CivAddress);
        Volatile.Write(ref _rfScope, new IcomLanRfScopeReceiver(radio.CivAddress));
        await control.WriteAsync([0x27, 0x10, 1], ct);
        await control.WriteAsync([0x27, 0x11, 1], ct);
    }

    private void ObserveScopePayload(ReadOnlyMemory<byte> payload)
    {
        if (!payload.IsEmpty) RfScope?.Observe(payload.Span);
    }
}
