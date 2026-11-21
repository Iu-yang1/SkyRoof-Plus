namespace SkyRoof.Remote.Foundation.Transmission;

public enum TransmitSourceKind { Manual, Digital }
public enum RfAction { None, Assert, Keep, Reconfigure, Release }

public sealed record RadioTxConfiguration(
    string DeviceId,
    string Mode,
    string ModulationInput,
    long FrequencyHz);

public sealed record ActiveTransmission(
    string TransactionId,
    string OwnerSessionId,
    TransmitSourceKind SourceKind,
    RadioTxConfiguration Radio,
    long Epoch);

public sealed record TransmitDecision(
    RfAction Action,
    ActiveTransmission? Previous,
    ActiveTransmission? Current,
    bool ClearPreviousAudio,
    bool InterruptedDigital,
    bool DigitalAutomationPaused,
    bool Changed);

public sealed class TransmitCoordinator(bool resumeDigitalAfterManual = false)
{
    private readonly object _sync = new();
    private ActiveTransmission? _active;
    private long _epoch;
    private bool _digitalAutomationPaused;

    public ActiveTransmission? Current
    {
        get { lock (_sync) return _active; }
    }

    public bool DigitalAutomationPaused
    {
        get { lock (_sync) return _digitalAutomationPaused; }
    }

    public TransmitDecision BeginManual(string transactionId, string sessionId, RadioTxConfiguration radio)
    {
        Validate(transactionId, sessionId, radio);
        lock (_sync)
        {
            if (_active?.TransactionId == transactionId && _active.OwnerSessionId == sessionId)
                return Decision(RfAction.None, _active, _active, false, false, false);

            var previous = _active;
            var interruptedDigital = previous?.SourceKind == TransmitSourceKind.Digital;
            if (interruptedDigital) _digitalAutomationPaused = true;
            _active = new ActiveTransmission(transactionId, sessionId, TransmitSourceKind.Manual, radio, ++_epoch);
            var action = previous is null ? RfAction.Assert
                : previous.Radio == radio ? RfAction.Keep : RfAction.Reconfigure;
            return Decision(action, previous, _active, previous is not null, interruptedDigital, true);
        }
    }

    // A scheduled digital TX cannot preempt an active manual TX.
    // A paused scheduler can be explicitly re-enabled independently of RX decoding.
    public TransmitDecision BeginDigital(string jobId, RadioTxConfiguration radio)
    {
        Validate(jobId, "scheduler", radio);
        lock (_sync)
        {
            if (_active?.TransactionId == jobId && _active.SourceKind == TransmitSourceKind.Digital)
                return Decision(RfAction.None, _active, _active, false, false, false);
            if (_active is not null || _digitalAutomationPaused)
                return Decision(RfAction.None, _active, _active, false, false, false);
            _active = new ActiveTransmission(jobId, "scheduler", TransmitSourceKind.Digital, radio, ++_epoch);
            return Decision(RfAction.Assert, null, _active, false, false, true);
        }
    }

    // Stale releases from a superseded operator never release the newer TX.
    public TransmitDecision ReleaseManual(string transactionId, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_sync)
        {
            if (_active is not { SourceKind: TransmitSourceKind.Manual } current
                || current.TransactionId != transactionId
                || current.OwnerSessionId != sessionId)
                return Decision(RfAction.None, _active, _active, false, false, false);

            _active = null;
            ++_epoch;
            if (resumeDigitalAfterManual) _digitalAutomationPaused = false;
            return Decision(RfAction.Release, current, null, true, false, true);
        }
    }

    public TransmitDecision CompleteDigital(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        lock (_sync)
        {
            if (_active is not { SourceKind: TransmitSourceKind.Digital } current
                || current.TransactionId != jobId)
                return Decision(RfAction.None, _active, _active, false, false, false);
            _active = null;
            ++_epoch;
            return Decision(RfAction.Release, current, null, true, false, true);
        }
    }

    public TransmitDecision Disconnect(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_sync)
        {
            if (_active is not { SourceKind: TransmitSourceKind.Manual } current
                || current.OwnerSessionId != sessionId)
                return Decision(RfAction.None, _active, _active, false, false, false);
            _active = null;
            ++_epoch;
            if (resumeDigitalAfterManual) _digitalAutomationPaused = false;
            return Decision(RfAction.Release, current, null, true, false, true);
        }
    }

    public void EnableDigitalAutomation()
    {
        lock (_sync) _digitalAutomationPaused = false;
    }

    public TransmitDecision StopAll()
    {
        lock (_sync)
        {
            var previous = _active;
            _active = null;
            _digitalAutomationPaused = true;
            ++_epoch;
            return Decision(previous is null ? RfAction.None : RfAction.Release,
                previous, null, previous is not null, false, previous is not null);
        }
    }

    // Process restart never reconstructs an interrupted manual transmission.
    public TransmitDecision RecoverAfterRestart()
    {
        lock (_sync)
        {
            var previous = _active;
            _active = null;
            _digitalAutomationPaused = true;
            ++_epoch;
            return Decision(previous is null ? RfAction.None : RfAction.Release,
                previous, null, previous is not null, false, previous is not null);
        }
    }

    private TransmitDecision Decision(RfAction action, ActiveTransmission? previous,
        ActiveTransmission? current, bool clear, bool interrupted, bool changed)
        => new(action, previous, current, clear, interrupted, _digitalAutomationPaused, changed);

    private static void Validate(string transactionId, string sessionId, RadioTxConfiguration radio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(radio);
        ArgumentException.ThrowIfNullOrWhiteSpace(radio.DeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(radio.Mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(radio.ModulationInput);
    }
}
