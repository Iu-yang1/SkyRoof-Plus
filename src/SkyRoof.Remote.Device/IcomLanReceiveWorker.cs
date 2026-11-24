using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Device;

/// <summary>
/// Optional owned native Icom LAN receive path, independent of SkyCAT CAT.
/// Opens control grant with the same reserved local CI-V/Audio socket ports,
/// then forwards validated raw UDP frames to Media's private SignalBus ingress.
/// </summary>
internal sealed partial class IcomLanReceiveWorker(ILogger<IcomLanReceiveWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var address = Environment.GetEnvironmentVariable("SKYROOF_ICOM_LAN_HOST")
            ?? throw new InvalidOperationException("Missing SKYROOF_ICOM_LAN_HOST.");
        var user = Environment.GetEnvironmentVariable("SKYROOF_ICOM_LAN_USERNAME")
            ?? throw new InvalidOperationException("Missing SKYROOF_ICOM_LAN_USERNAME.");
        var password = Environment.GetEnvironmentVariable("SKYROOF_ICOM_LAN_PASSWORD")
            ?? throw new InvalidOperationException("Missing SKYROOF_ICOM_LAN_PASSWORD.");
        int port = int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_ICOM_LAN_PORT"),
            out int selected) ? selected : 50001;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(address, port, user, password, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Native Icom RX session needs a fresh control/media grant.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunSessionAsync(string host, int port,
        string user, string password, CancellationToken ct)
    {
        await using var civ = IcomLanCivSocket.Reserve(host, port);
        await using var audio = IcomLanAudioSocket.Reserve(host, port);
        var rate = uint.TryParse(Environment.GetEnvironmentVariable(
            "SKYROOF_ICOM_LAN_RX_RATE"), out var preferred) ? preferred : 48000;
        var codecValue = byte.TryParse(Environment.GetEnvironmentVariable(
            "SKYROOF_ICOM_LAN_RX_CODEC"), out var selectedCodec) ? selectedCodec : (byte)4;
        var codec = (IcomLanAudioCodec)codecValue;
        if (!Enum.IsDefined(codec))
            throw new ArgumentException("Unsupported native Icom LAN RX codec.");

        await using var control = await IcomLanControlConnection.OpenAsync(new(
            host, port, user, password, "SkyRoof Remote",
            civ.LocalPort, audio.LocalPort, rate, codecValue), ct);
        if (control.NegotiatedRxRate == 0)
            throw new InvalidOperationException("Radio capabilities do not offer an RX audio rate.");
        await civ.ConnectAsync(host, control.RemoteCivPort, ct);
        await audio.ConnectAsync(host, control.RemoteAudioPort, ct);
        logger.LogInformation("Native Icom RX session attached: model {Model}, audio UDP {Port}, {Rate} Hz",
            control.Capabilities?.RadioName, control.RemoteAudioPort, control.NegotiatedRxRate);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = RunAudioToMediaAsync(audio, (int)control.NegotiatedRxRate,
            codec, lifetime.Token);
        var finished = await Task.WhenAny(pump,
            control.MaintenanceTask, civ.MaintenanceTask);
        lifetime.Cancel();
        await finished;
    }
}
