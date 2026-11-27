namespace SkyRoof.Remote.Foundation.Transmission;

public sealed partial class TransmitCoordinator
{
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
            if (_resumeDigitalAfterManual) _digitalAutomationPaused = false;
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
            if (_resumeDigitalAfterManual) _digitalAutomationPaused = false;
            return Decision(RfAction.Release, current, null, true, false, true);
        }
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

}
