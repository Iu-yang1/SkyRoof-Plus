using SkyRoof.Remote.Foundation.IcomLan;

namespace SkyRoof.Remote.Device;

// Only the existing Direct LAN worker can publish a CAT session; no second
// login or independently owned CI-V socket is opened by the CAT backend.
internal sealed class NativeIcomCatSession
{
    private Attached? _session;
    private sealed record Attached(IcomLanSatelliteCat Cat, NativeIcomTxAudio? Audio);
    public IcomLanSatelliteCat Current => Volatile.Read(ref _session)?.Cat
        ?? throw new IOException("Native Icom LAN CAT session is reconnecting.");
    public NativeIcomTxAudio? Tx => Volatile.Read(ref _session)?.Audio;
    public void Attach(IcomLanSatelliteCat session, NativeIcomTxAudio? audio) =>
        Volatile.Write(ref _session, new Attached(session, audio));
    public void Detach() => Volatile.Write(ref _session, null);
}
