using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    private async Task<(string Description, bool Changed)> BeginAsync(
        DeviceCommand command, bool digital, CancellationToken token)
    {
        RequireTransaction(command);
        if (digital && (_digitalPaused || _owner is not null)) return ("digital-busy", false);
        if (_owner?.TransactionId == command.TransactionId &&
            _owner.SessionId == command.SessionId && _owner.Digital == digital)
            return ("duplicate", false);

        var requestedHz = command.FrequencyHz > 0 ? command.FrequencyHz : _txHz;
        var requestedMode = command.Mode.Length > 0 ? command.Mode : _txMode;
        var requestedSource = command.AudioSource.Length > 0 ? command.AudioSource
            : _owner?.AudioSource ?? "LAN";
        if (requestedHz <= 0) requestedHz = await cat.ReadTxFrequencyAsync(token);
        var existing = _owner;
        var same = existing is not null && existing.FrequencyHz == requestedHz &&
            existing.Mode == requestedMode && existing.AudioSource == requestedSource;
        var transmitting = await cat.ReadPttAsync(token);
        if (existing is null && transmitting)
            throw new InvalidOperationException("PTT is asserted by an external controller.");
        if (digital && existing is not null) return ("digital-busy", false);
        if (!digital && existing?.Digital == true) _digitalPaused = true;

        if (existing is not null && same && transmitting)
        {
            journal.Save(command.TransactionId, command.SessionId);
            _owner = new DeviceOwner(command.TransactionId, command.SessionId, digital,
                requestedHz, requestedMode, requestedSource, ++_epoch);
            return ("source-takeover-no-rf-transition", true);
        }

        // When an existing transmission changes hardware configuration, first
        // release the currently owned RF transaction, then reapply CAT settings.
        if (existing is not null && transmitting)
        {
            await cat.SetPttAsync(false, token);
            if (await cat.ReadPttAsync(token))
                throw new IOException("PTT remained ON after explicit release.");
            _owner = null;
        }
        if (requestedHz != _txHz)
        {
            await cat.SetTxFrequencyAsync(requestedHz, token);
            _txHz = requestedHz;
        }
        if (requestedMode.Length > 0 && requestedMode != _txMode)
        {
            await cat.SetTxModeAsync(requestedMode, token);
            _txMode = requestedMode;
        }
        journal.Save(command.TransactionId, command.SessionId);
        await cat.SetPttAsync(true, token);
        if (!await cat.ReadPttAsync(token))
            throw new IOException("PTT assertion not confirmed by CAT readback.");
        _owner = new DeviceOwner(command.TransactionId, command.SessionId, digital,
            requestedHz, requestedMode, requestedSource, ++_epoch);
        return (existing is null ? "ptt-asserted" : "rf-reconfigured", true);
    }

    private async Task<(string Description, bool Changed)> EndAsync(
        DeviceCommand command, bool digital, CancellationToken token)
    {
        RequireTransaction(command);
        var current = _owner;
        if (current is null || current.TransactionId != command.TransactionId ||
            current.SessionId != command.SessionId || current.Digital != digital)
            return ("stale-release-ignored", false);
        if (await cat.ReadPttAsync(token))
        {
            await cat.SetPttAsync(false, token);
            if (await cat.ReadPttAsync(token))
                throw new IOException("PTT remained ON after release.");
        }
        journal.Clear();
        _owner = null;
        ++_epoch;
        return ("ptt-released", true);
    }

    private (string Description, bool Changed) ResumeDigital()
    {
        var wasPaused = _digitalPaused;
        _digitalPaused = false;
        return ("digital-automation-enabled", wasPaused);
    }

    private async Task<(string Description, bool Changed)> ReleaseSessionAsync(
        string sessionId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.");
        if (_owner is not { Digital: false } active || active.SessionId != sessionId)
            return ("session-not-owner", false);
        if (await cat.ReadPttAsync(token))
        {
            await cat.SetPttAsync(false, token);
            if (await cat.ReadPttAsync(token))
                throw new IOException("PTT remained ON after disconnect.");
        }
        journal.Clear();
        _owner = null;
        ++_epoch;
        return ("session-released", true);
    }

    private async Task<(string Description, bool Changed)> StopAsync(CancellationToken token)
    {
        _digitalPaused = true;
        if (_owner is null) return ("no-owned-transmission", false);
        if (await cat.ReadPttAsync(token))
        {
            await cat.SetPttAsync(false, token);
            if (await cat.ReadPttAsync(token))
                throw new IOException("PTT remained ON after STOP.");
        }
        journal.Clear();
        _owner = null;
        ++_epoch;
        return ("owned-ptt-released", true);
    }
}
