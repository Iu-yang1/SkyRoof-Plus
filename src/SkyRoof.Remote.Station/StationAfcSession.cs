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
    private readonly StationAfcPolarityProbe? probe;
    private double? center;
    private DateTime lastContact = DateTime.UtcNow;
    private bool ptt, duplex;
    private string rxMode = "";
    private double lastApplied;
    private long minimumGeneration = -1, observedGeneration = -1;
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
        if (input.AutoCalibrate && input.Sign == 0)
            probe = new StationAfcPolarityProbe(input.MaxCorrectionHz, DateTime.UtcNow);
        center = input.HasCenterHz ? input.CenterHz : null;
        controller.Configure(mode, control);
        controller.ConfigureSignalLoss(input.GentleRelease ? AfcSignalLossPolicy.GentleRelease : AfcSignalLossPolicy.HoldLast);
        if (input.HasCenterHz) controller.CenterOn(input.CenterHz);
        else controller.LockHere();
    }

    internal AfcSessionStatus Update(AfcMeasurement? measurement, AfcTargetChange? target, DateTime now)
    {
        lastContact = now;
        if (target is not null) Retarget(target);
        if (measurement is not null) Observe(measurement, now);
        probe?.Tick(now);
        controller.Tick(now, ptt);
        return Status();
    }

    private void Retarget(AfcTargetChange target)
    {
        if (target.Generation < 0 || (target.HasCenterHz && !double.IsFinite(target.CenterHz)))
            throw new ArgumentException("Invalid AFC target generation or center.");
        if (target.Generation == minimumGeneration) return;
        if (target.Generation <= observedGeneration || target.Generation < minimumGeneration)
            throw new ArgumentException("Stale AFC target generation.");
        minimumGeneration = target.Generation;
        center = target.HasCenterHz ? target.CenterHz : null;
        probe?.Restart(DateTime.UtcNow);
        controller.OnUserTune();
        if (target.HasCenterHz) controller.CenterOn(target.CenterHz);
        else controller.LockHere();
    }

    private void Observe(AfcMeasurement measurement, DateTime now)
    {
        if (measurement.Generation < Math.Max(minimumGeneration, observedGeneration)) return;
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(measurement.SampleUtcMilliseconds).UtcDateTime;
        if (utc > now.AddMilliseconds(250) || utc < now.AddSeconds(-2)) return;
        observedGeneration = measurement.Generation;
        bool aligned = timeline.TryReadbackAt(utc, out long frequency);
        bool attributed = timeline.TryAttributedReadbackAt(utc, AfcCatWriteSource.Afc, out _);
        int nominal = rxMode.Length == 0 ? 0 : rxMode.StartsWith("LSB", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        if (probe is { Verified: false } calibration)
        {
            bool verified = calibration.Observe(measurement.IfOffsetHz, measurement.SigmaHz, measurement.Confidence,
                measurement.AbsoluteReference, measurement.Ambiguous, aligned && duplex && !ptt, measurement.Generation, utc);
            if (!verified) return;
            controller.OnUserTune();
            if (center is double value) controller.CenterOn(value);
            else controller.LockHere();
        }
        var observation = new AfcObservation(mode, utc, measurement.Generation, measurement.IfOffsetHz,
            measurement.SigmaHz, measurement.Confidence, measurement.AbsoluteReference, measurement.Ambiguous);
        controller.Observe(observation, new AfcObservationContext(frequency, probe?.Sign ?? config.Sign, config.MaxCorrectionHz,
            config.FmDeadbandHz, config.ToneDeadbandHz, ptt, aligned, attributed, !aligned || !duplex, nominal));
    }

    internal void Apply(TrackingSnapshot prediction, DateTime now)
    {
        probe?.Tick(now);
        controller.Tick(now, ptt);
        bool calibrating = probe is { Verified: false };
        double correction = prediction.RxHz > 0 ? (calibrating ? probe!.Bias : controller.GetRxCorrection(ptt)) : 0;
        if (ptt) correction = lastApplied;
        if (!duplex) correction = 0;
        if (prediction.RxHz <= 0) correction = 0;
        long target = checked(prediction.RxHz + (long)Math.Round(correction));
        if (target <= 0) return;
        prediction.RxHz = target;
        prediction.AfcCorrectionHz = correction;
        prediction.AfcHoldCorrectionHz = (long)Math.Round(lastApplied);
        timeline.RecordRequest(target, now, calibrating ? AfcCatWriteSource.Calibration : correction != lastApplied ? AfcCatWriteSource.Afc : AfcCatWriteSource.SatelliteModel);
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
        probe?.Readback(duplex, ptt, rxMode, applied, utc);
        if (applied == (long)Math.Round(snapshot.AfcCorrectionHz) || applied == snapshot.AfcHoldCorrectionHz)
            lastApplied = applied;
    }

    internal AfcSessionStatus Status()
    {
        var current = controller.Snapshot;
        return new AfcSessionStatus { SessionId = Id, JobId = JobId, State = current.State.ToString(),
            Detail = probe is { Verified: false } calibration ? "RX polarity calibration: " + calibration.State : current.Detail,
            CalibrationState = probe?.State ?? "Disabled", ObservedHz = current.ObservedIfOffsetHz,
            CorrectionHz = probe is { Verified: false } ? lastApplied : current.CorrectionHz,
            ErrorHz = current.ErrorHz, Confidence = current.Confidence, Sign = probe?.Sign ?? current.MappingSign };
    }
}
