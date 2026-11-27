using SkyRoof.Remote.Foundation.IcomLan;

namespace SkyRoof.Remote.Device;

internal sealed partial class IcomLanReceiveWorker
{
    private async Task ProbeNativeCatAsync(IcomLanCivSocket civ,
        IcomLanRadioCapabilities? capabilities, CancellationToken ct)
    {
        var radio = capabilities ?? throw new IOException("Missing authenticated radio capabilities.");
        var readbacks = new IcomLanCivReadbacks(civ, radio.CivAddress);
        long frequency = await readbacks.ReadSelectedFrequencyAsync(ct);
        bool transmitting = await readbacks.ReadPttAsync(ct);
        logger.LogInformation("Native CI-V probe: selected frequency {Hz} Hz, PTT {Ptt}",
            frequency, transmitting);
    }
}
