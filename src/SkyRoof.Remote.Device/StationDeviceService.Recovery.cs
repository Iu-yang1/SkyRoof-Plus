using Grpc.Core;
using SkyRoof.Remote.Ipc;
using System.Net.Sockets;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    public async Task RecoverAfterRestartAsync(CancellationToken token)
    {
        await _commands.WaitAsync(token);
        try { await RecoverOrphanedPttAsync(token); }
        finally { _commands.Release(); }
    }

    // A fresh authenticated session must never inherit an old TX owner.
    // The durable journal is kept until RX is confirmed on the new session.
    internal async Task InvalidateNativeSessionAsync()
    {
        await _commands.WaitAsync();
        try
        {
            if (_cwActive is { Keyer: NativeCwKeyer } cw)
            {
                _cwActive = null;
                cw.Stopped.TrySetResult();
            }
            if (_owner is null && !journal.Exists) return;
            _owner = null;
            _digitalPaused = true;
            ++_epoch;
            ++_revision;
        }
        finally { _commands.Release(); }
    }

    private async Task RecoverOrphanedPttAsync(CancellationToken token)
    {
        if (_owner is not null || _cwActive is not null || journal.Read() is not { } record) return;
        if (record.TransactionId.StartsWith("native-cw:", StringComparison.Ordinal))
        {
            if (cat is not NativeIcomCatBackend native)
                throw new InvalidOperationException("Native CW recovery requires original backend.");
            await using var keyer = native.CreateCwKeyer();
            await keyer.StopAsync(token);
            await FinishNativeCwAsync(token);
            _digitalPaused = true;
            ++_epoch;
            ++_revision;
            return;
        }
        if (await cat.ReadPttAsync(token))
        {
            await cat.SetPttAsync(false, token);
            if (await cat.ReadPttAsync(token))
                throw new IOException("Unable to release previously owned PTT after restart.");
        }
        journal.Clear();
        _digitalPaused = true;
        ++_revision;
        ++_epoch;
    }
    private async Task FinishNativeCwAsync(CancellationToken token)
    {
        // Semi break-in can keep TX asserted for a short interval after STOP.
        for (int i = 0; i < 20; i++)
        {
            if (!await cat.ReadPttAsync(token))
            {
                journal.Clear();
                return;
            }
            await Task.Delay(100, token);
        }
        await cat.SetPttAsync(false, token);
        if (await cat.ReadPttAsync(token)) throw new IOException("Native CW STOP did not return RX.");
        journal.Clear();
    }
}

