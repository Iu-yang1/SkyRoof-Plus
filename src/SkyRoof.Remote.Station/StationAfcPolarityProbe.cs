using SkyRoof.AFC;

namespace SkyRoof.Remote.Station;

// One bounded RX-only offset, followed by an independently observed restore.
// Measured IF deltas must agree in both directions; model-only Doppler is
// not treated as evidence of the hardware's IF axis.
internal sealed class StationAfcPolarityProbe
{
    private enum Phase { Baseline, Probe, Restore, AbortRestore, Verified, Failed }
    private readonly double amplitude;
    private Phase phase;
    private DateTime entered, confirmed;
    private double baseline, displaced;
    private long generationMode;
    private string radioMode = "";
    internal int Sign { get; private set; }
    internal bool Verified => phase == Phase.Verified;
    internal double Bias => phase == Phase.Probe ? amplitude : 0;
    internal string State => phase.ToString();

    internal StationAfcPolarityProbe(double maximumCorrection, DateTime now)
    {
        amplitude = Math.Min(150, Math.Floor(maximumCorrection));
        if (amplitude < 50) throw new ArgumentException("Polarity probe needs a correction range of at least 50 Hz.");
        Restart(now);
    }

    internal void Restart(DateTime now)
    {
        Sign = 0;
        radioMode = "";
        generationMode = 0;
        Move(Phase.Baseline, now);
    }

    private void Move(Phase next, DateTime now)
    {
        phase = next; entered = now; confirmed = default;
    }

    internal void Tick(DateTime now)
    {
        if ((phase is Phase.Baseline or Phase.Probe or Phase.Restore) &&
            now - entered > TimeSpan.FromSeconds(15))
            Move(Phase.AbortRestore, now);
    }

    internal void Readback(bool duplex, bool ptt, string mode, double actualBias, DateTime utc)
    {
        if (phase == Phase.Failed) return;
        if (radioMode.Length != 0 && mode != radioMode)
        {
            Sign = 0;
            Move(Phase.AbortRestore, utc);
        }
        if (radioMode.Length == 0 && mode.Length > 0) radioMode = mode;
        if (phase == Phase.Verified) return;
        if (ptt || !duplex)
        {
            if (phase != Phase.AbortRestore) Move(Phase.AbortRestore, utc);
            return;
        }
        if (utc < entered || Math.Abs(actualBias - Bias) > 3)
        { confirmed = default; return; }
        if (confirmed == default) confirmed = utc;
        if (phase == Phase.AbortRestore) Move(Phase.Failed, utc);
    }

    internal bool Observe(AfcObservation observation, bool aligned)
    {
        double offsetHz = observation.IfOffsetHz, sigma = observation.SigmaHz, confidence = observation.Confidence;
        bool absolute = observation.HasAbsoluteReference, ambiguous = observation.Ambiguous;
        long generation = observation.Generation;
        DateTime utc = observation.SampleUtc;
        if (phase is Phase.Verified or Phase.Failed or Phase.AbortRestore || !aligned ||
            !absolute || ambiguous || !double.IsFinite(offsetHz) || !double.IsFinite(sigma) ||
            !double.IsFinite(confidence) || confidence < .70 || sigma < 0 || sigma > 15 ||
            confirmed == default || utc < confirmed.AddMilliseconds(650)) return false;
        if (phase != Phase.Baseline && generation != generationMode)
        {
            Move(Phase.AbortRestore, utc);
            return false;
        }
        switch (phase)
        {
            case Phase.Baseline:
                baseline = offsetHz;
                generationMode = generation;
                Move(Phase.Probe, utc);
                break;
            case Phase.Probe:
                displaced = offsetHz;
                Move(Phase.Restore, utc);
                break;
            case Phase.Restore:
                double up = displaced - baseline, down = displaced - offsetHz;
                double tolerance = Math.Max(25, amplitude * .35);
                if (Math.Abs(Math.Abs(up) - amplitude) <= tolerance &&
                    Math.Abs(Math.Abs(down) - amplitude) <= tolerance &&
                    Math.Sign(up) == Math.Sign(down) && Math.Abs(offsetHz - baseline) <= tolerance)
                {
                    Sign = -Math.Sign(up);
                    Move(Phase.Verified, utc);
                    return true;
                }
                Move(Phase.Failed, utc);
                break;
        }
        return false;
    }
}
