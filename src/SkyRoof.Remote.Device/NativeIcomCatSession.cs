using SkyRoof.Remote.Foundation.IcomLan;

namespace SkyRoof.Remote.Device;

// Only the existing Direct LAN worker can publish a CAT session; no second
// login or independently owned CI-V socket is opened by the CAT backend.
internal sealed class NativeIcomCatSession
{
    private IcomLanSatelliteCat? _session;
    public IcomLanSatelliteCat Current => Volatile.Read(ref _session)
        ?? throw new IOException("Native Icom LAN CAT session is reconnecting.");
    public void Attach(IcomLanSatelliteCat session) => Volatile.Write(ref _session, session);
    public void Detach() => Volatile.Write(ref _session, null);
}
