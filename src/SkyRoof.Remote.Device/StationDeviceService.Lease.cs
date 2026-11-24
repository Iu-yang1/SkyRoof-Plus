using System.Diagnostics;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    public async Task<bool> ExpireManualLeaseAsync(TimeSpan threshold, CancellationToken token)
    {
        await _commands.WaitAsync(token);
        try
        {
            if (_owner is null && journal.Exists)
                await RecoverOrphanedPttAsync(token);
            if (_owner is not { Digital: false } ||
                _lastManualBeat == 0 ||
                Stopwatch.GetElapsedTime(_lastManualBeat) <= threshold)
                return false;
            if (await cat.ReadPttAsync(token))
            {
                await cat.SetPttAsync(false, token);
                if (await cat.ReadPttAsync(token))
                    throw new IOException("PTT remained ON after manual session lease expired.");
            }
            journal.Clear();
            _owner = null;
            _lastManualBeat = 0;
            ++_epoch;
            ++_revision;
            return true;
        }
        finally { _commands.Release(); }
    }
}
