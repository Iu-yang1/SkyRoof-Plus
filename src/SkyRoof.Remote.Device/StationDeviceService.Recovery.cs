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

    private async Task RecoverOrphanedPttAsync(CancellationToken token)
    {
        if (_owner is not null || journal.Read() is null) return;
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


}
