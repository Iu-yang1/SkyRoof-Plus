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

public sealed partial class TransmitCoordinator
{
    private readonly bool _resumeDigitalAfterManual;
    public TransmitCoordinator(bool resumeDigitalAfterManual = false) => _resumeDigitalAfterManual = resumeDigitalAfterManual;
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
