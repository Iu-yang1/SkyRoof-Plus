using SkyRoof.AFC;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

internal sealed class StationAfcSession
{
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal string JobId => config.JobId;
    private readonly AfcOpenRequest config;
    private readonly AfcController controller = new();
    private readonly AfcCatTimeline timeline = new();
    private readonly AfcSignalMode mode;
    private readonly AfcControlMode control;
    private DateTime lastContact = DateTime.UtcNow;
    private bool ptt, duplex;
    private string rxMode = "";
    private double lastApplied;
    internal bool Expired(DateTime now) => now - lastContact > TimeSpan.FromSeconds(8);

    internal StationAfcSession(AfcOpenRequest input)
    {
        config = input.Clone();
        if (!Enum.TryParse(input.Mode, true, out mode) || !Enum.IsDefined(mode) ||
            !Enum.TryParse(input.Control, true, out control) || control is not (AfcControlMode.Assist or AfcControlMode.Auto) ||
            input.Sign is < -1 or > 1 || !double.IsFinite(input.MaxCorrectionHz) || input.MaxCorrectionHz <= 0 ||
            !double.IsFinite(input.FmDeadbandHz) || input.FmDeadbandHz <= 0 ||
            !double.IsFinite(input.ToneDeadbandHz) || input.ToneDeadbandHz <= 0 ||
            (input.HasCenterHz && !double.IsFinite(input.CenterHz)))
            throw new ArgumentException("Invalid AFC control profile.");
        controller.Configure(mode, control);
        controller.ConfigureSignalLoss(input.GentleRelease ? AfcSignalLossPolicy.GentleRelease : AfcSignalLossPolicy.HoldLast);
        if (input.HasCenterHz) controller.CenterOn(input.CenterHz);
        else controller.LockHere();
    }

    internal AfcSessionStatus Update(AfcMeasurement? measurement, DateTime now)
    {
        lastContact = now;
        if (measurement is not null) Observe(measurement, now);
        controller.Tick(now, ptt);
        return Status();
    }

    private void Observe(AfcMeasurement measurement, DateTime now)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(measurement.SampleUtcMilliseconds).UtcDateTime;
        if (utc > now.AddMilliseconds(250) || utc < now.AddSeconds(-2)) return;
        bool aligned = timeline.TryReadbackAt(utc, out long frequency);
        bool attributed = timeline.TryAttributedReadbackAt(utc, AfcCatWriteSource.Afc, out _);
        int nominal = rxMode.Length == 0 ? 0 : rxMode.StartsWith("LSB", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        var observation = new AfcObservation(mode, utc, measurement.Generation, measurement.IfOffsetHz,
            measurement.SigmaHz, measurement.Confidence, measurement.AbsoluteReference, measurement.Ambiguous);
        controller.Observe(observation, new AfcObservationContext(frequency, config.Sign, config.MaxCorrectionHz,
            config.FmDeadbandHz, config.ToneDeadbandHz, ptt, aligned, attributed, !aligned || !duplex, nominal));
    }

    internal void Apply(TrackingSnapshot prediction, DateTime now)
    {
        controller.Tick(now, ptt);
        double correction = prediction.RxHz > 0 ? controller.GetRxCorrection(ptt) : 0;
        if (ptt) correction = lastApplied;
        if (!duplex) correction = 0;
        if (prediction.RxHz <= 0) correction = 0;
        long target = checked(prediction.RxHz + (long)Math.Round(correction));
        if (target <= 0) return;
        prediction.RxHz = target;
        prediction.AfcCorrectionHz = correction;
        prediction.AfcHoldCorrectionHz = (long)Math.Round(lastApplied);
        timeline.RecordRequest(target, now, correction != lastApplied ? AfcCatWriteSource.Afc : AfcCatWriteSource.SatelliteModel);
    }

    internal void Readback(TrackingSnapshot snapshot)
    {
        if (!snapshot.CatApplied || snapshot.CatReadbackUtcMilliseconds <= 0) return;
        ptt = snapshot.ActualPtt;
        duplex = snapshot.SupportsDuplexFrequencies;
        rxMode = snapshot.ActualRxMode;
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.CatReadbackUtcMilliseconds).UtcDateTime;
        timeline.RecordReadback(snapshot.ActualRxHz, utc);
        long nominal = snapshot.RxHz - (long)Math.Round(snapshot.AfcCorrectionHz);
        long applied = snapshot.ActualRxHz - nominal;
        if (applied == (long)Math.Round(snapshot.AfcCorrectionHz) || applied == snapshot.AfcHoldCorrectionHz)
            lastApplied = applied;
    }

    internal AfcSessionStatus Status()
    {
        var current = controller.Snapshot;
        return new AfcSessionStatus { SessionId = Id, JobId = JobId, State = current.State.ToString(),
            Detail = current.Detail, ObservedHz = current.ObservedIfOffsetHz, CorrectionHz = current.CorrectionHz,
            ErrorHz = current.ErrorHz, Confidence = current.Confidence, Sign = current.MappingSign };
    }
}
