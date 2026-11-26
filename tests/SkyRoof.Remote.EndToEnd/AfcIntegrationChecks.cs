using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;

internal static class AfcIntegrationChecks
{
    internal static async Task RunAsync(string pipe, string jobId)
    {
        using var client = new StationIpcClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var ct = timeout.Token;
        var opened = await client.OpenAfcAsync(new AfcOpenRequest { JobId = jobId, Mode = "CW",
            Control = "Auto", Sign = 1, MaxCorrectionHz = 2000, FmDeadbandHz = 20,
            ToneDeadbandHz = 5, CenterHz = 0 }, ct);
        try
        {
            await VerifyCorrectionAsync(client, opened.SessionId, ct);
        }
        finally { await client.CloseAfcAsync(opened.SessionId, CancellationToken.None); }
        for (int i = 0; i < 30; i++)
        {
            var status = await client.TrackingAsync(ct);
            if (status.Latest is { CatApplied: true, AfcCorrectionHz: 0 })
            {
                Console.WriteLine("PASS: Station AFC uses aligned CAT feedback and RX-only correction; close removes transient bias.");
                return;
            }
            await Task.Delay(150, ct);
        }
        throw new InvalidDataException("AFC bias not removed after close.");
    }

    private static async Task VerifyCorrectionAsync(StationIpcClient client, string sessionId, CancellationToken ct)
    {
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(250, ct);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await client.UpdateAfcAsync(new AfcUpdateRequest { SessionId = sessionId,
                Measurement = new AfcMeasurement { SampleUtcMilliseconds = now, Generation = 1, IfOffsetHz = 90,
                    SigmaHz = 3, Confidence = 1, AbsoluteReference = true } }, ct);
            var snapshot = (await client.TrackingAsync(ct)).Latest;
            if (snapshot is not { CatApplied: true } || Math.Abs(snapshot.AfcCorrectionHz) < 5) continue;
            double factor = snapshot.RangeRateMetresPerSecond / 299792458.0;
            long expectedRx = (long)Math.Round(145900000 * (1 - factor)) + (long)Math.Round(snapshot.AfcCorrectionHz);
            long expectedTx = (long)Math.Round(435650000 * (1 + factor));
            if (snapshot.ActualRxHz != expectedRx || snapshot.ActualTxHz != expectedTx || snapshot.ActualPtt)
                throw new InvalidDataException("AFC changed TX or RX did not follow the composed bias.");
            return;
        }
        throw new InvalidDataException("Aligned AFC observations did not produce a CAT RX bias.");
    }
}
