using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Host.Realtime;

internal static class RadioLinkChecks
{
    internal static async Task RunAsync(HubConnection hub, RemoteTrackingRequest baseline,
        RemoteTrackingStatus prediction, FakeRigctld radio)
    {
        var link = new RemoteRadioLink
        {
            DownlinkLowHz = 145_900_000, DownlinkHighHz = 145_920_000,
            UplinkLowHz = 435_630_000, UplinkHighHz = 435_650_000,
            Inverting = true, DownlinkBaseOffsetHz = 400, UplinkBaseOffsetHz = -300,
            TransponderOffsetHz = 25_000, RitHz = 100, XitHz = 200,
            DownlinkManualHz = -90, UplinkManualHz = 80
        };
        var request = baseline with { RadioLink = link, PointRotator = false };
        var result = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request);
        double factor = prediction.RangeRateMetresPerSecond / 299_792_458.0;
        long rx = (long)Math.Round((145_900_000 + 400 + 25_000 + 100) * (1 - factor) - 90);
        long tx = (long)Math.Round((435_650_000 - 300 - 25_000) * (1 + factor) + 80 + 200);
        Check(result.RxHz == rx && result.TxHz == tx, "Inverting RadioLink or RIT/XIT correction differs.");
        result = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking",
            request with { RadioLink = link with { Inverting = false } });
        tx = (long)Math.Round((435_630_000 - 300 + 25_000) * (1 + factor) + 80 + 200);
        Check(result.TxHz == tx, "Non-inverting RadioLink position mapping differs.");

        var only = request with { RadioLink = new RemoteRadioLink { UplinkLowHz = 435_660_000 } };
        var applied = await hub.InvokeAsync<RemoteTrackingStatus>("ApplyTracking", only);
        Check(applied.RxHz == 0 && applied.ActualRxHz == prediction.RxHz &&
            applied.ActualTxHz == applied.TxHz && radio.Count("F 0") == 0,
            "Uplink-only tracking modified RX or wrote zero Hz.");
        Check(radio.Count("T 1") == 1, "RadioLink tuning asserted PTT.");
        Console.WriteLine("PASS: shared RadioLink inverse/non-inverse mapping, unclamped position, offsets, uplink-only CAT.");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
