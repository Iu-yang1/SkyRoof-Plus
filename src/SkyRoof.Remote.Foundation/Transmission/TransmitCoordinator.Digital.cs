namespace SkyRoof.Remote.Foundation.Transmission;

public sealed partial class TransmitCoordinator
{
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

    public void EnableDigitalAutomation()
    {
        lock (_sync) _digitalAutomationPaused = false;
    }

}
