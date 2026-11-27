using SkyRoof.AFC;
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
            await VerifyRetargetAsync(client, opened.SessionId, ct);
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

    private static async Task VerifyRetargetAsync(StationIpcClient client, string sessionId, CancellationToken ct)
    {
        var rebased = await client.UpdateAfcAsync(new AfcUpdateRequest
        {
            SessionId = sessionId, Target = new AfcTargetChange { Generation = 2, CenterHz = 90 }
        }, ct);
        if (rebased.SessionId != sessionId || rebased.CorrectionHz != 0)
            throw new InvalidDataException("AFC retarget did not rebase correction in the same session.");
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(220, ct);
            var stale = await client.UpdateAfcAsync(TargetMeasurement(sessionId, 1, 600), ct);
            if (stale.CorrectionHz != 0 || Math.Abs(stale.ObservedHz - 600) < 1)
                throw new InvalidDataException("Old IF generation affected the retargeted AFC session.");
        }
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(220, ct);
            var current = await client.UpdateAfcAsync(TargetMeasurement(sessionId, 2, 90), ct);
            if (Math.Abs(current.ObservedHz - 90) < 1 && current.State == "Locked" && Math.Abs(current.CorrectionHz) < 1)
            {
                Console.WriteLine("PASS: AFC live target change, stale generation ignored, new carrier acquired within same session.");
                return;
            }
        }
        throw new InvalidDataException("AFC did not reacquire the new generation.");
    }

    private static AfcUpdateRequest TargetMeasurement(string id, long generation, double offset) => new()
    {
        SessionId = id,
        Measurement = new AfcMeasurement
        {
            SampleUtcMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Generation = generation, IfOffsetHz = offset, SigmaHz = 1, Confidence = .99, AbsoluteReference = true
        }
    };

    private static async Task VerifyCorrectionAsync(StationIpcClient client, string sessionId, CancellationToken ct)
    {
        var processor = new AfcSignalProcessing();
        var pcm = Enumerable.Range(0, 48000).Select(i => (float)(.5 * Math.Sin(2 * Math.PI * 12090 * i / 48000))).ToArray();
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(250, ct);
            var observation = processor.Analyze(pcm, AfcSignalMode.CW, DateTime.UtcNow, 1, 90);
            if (!observation.HasAbsoluteReference || Math.Abs(observation.IfOffsetHz - 90) > 5)
                throw new InvalidDataException("True 12 kHz IF synthetic tone estimator failed.");
            await client.UpdateAfcAsync(new AfcUpdateRequest { SessionId = sessionId,
                Measurement = new AfcMeasurement { SampleUtcMilliseconds = new DateTimeOffset(observation.SampleUtc).ToUnixTimeMilliseconds(),
                    Generation = observation.Generation, IfOffsetHz = observation.IfOffsetHz, SigmaHz = observation.SigmaHz,
                    Confidence = observation.Confidence, AbsoluteReference = observation.HasAbsoluteReference, Ambiguous = observation.Ambiguous } }, ct);
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
