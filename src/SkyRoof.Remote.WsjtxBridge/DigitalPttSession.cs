using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.WsjtxBridge;

public sealed class DigitalPttSession(IRemoteRig rig, bool requireNativeAudio) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TxGrant? _grant;
    public TxGrant? Grant => Volatile.Read(ref _grant);
    public event Action<TxGrant?>? GrantChanged;

    public async Task<bool> SetAsync(bool on, long frequencyHz, string mode, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!on) { await ReleaseAsync(ct); return true; }
            if (Grant is { } existing)
            {
                var current = await rig.ReadAsync(ct);
                if (Matches(current, existing)) return true;
                SetGrant(null);
                return false;
            }
            var state = await rig.ReadAsync(ct);
            if (state.DigitalAutomationPaused || (requireNativeAudio && !state.NativeTxAvailable)) return false;
            string transaction = Guid.NewGuid().ToString("N");
            var keyed = await rig.ExecuteAsync(new RigCommand(transaction, DeviceOperation.DeviceDigitalBegin,
                frequencyHz, mode, "LAN"), ct);
            var grant = new TxGrant(transaction, keyed.Epoch);
            if (!Matches(keyed, grant)) return false;
            SetGrant(grant);
            return true;
        }
        finally { _gate.Release(); }
    }

    private bool Matches(RigStatus status, TxGrant grant) =>
        status.ActualPtt && status.ActiveSessionId == rig.SessionId &&
        status.ActiveTransactionId == grant.TransactionId && status.Epoch == grant.Epoch;

    private void SetGrant(TxGrant? grant)
    {
        Volatile.Write(ref _grant, grant);
        GrantChanged?.Invoke(grant);
    }

    private async Task ReleaseAsync(CancellationToken ct)
    {
        var previous = Grant;
        SetGrant(null);
        if (previous is not null)
            await rig.ExecuteAsync(new RigCommand(previous.TransactionId, DeviceOperation.DeviceDigitalEnd), ct);
    }

    public async Task HeartbeatAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (Grant is not { } active) continue;
                var status = await rig.ExecuteAsync(new RigCommand(active.TransactionId,
                    DeviceOperation.DeviceDigitalHeartbeat), ct);
                if (!Matches(status, active)) SetGrant(null);
            }
            finally { _gate.Release(); }
        }
    }

    public async Task AbortAsync(TxGrant expected)
    {
        await _gate.WaitAsync();
        try
        {
            if (Grant != expected) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ReleaseAsync(timeout.Token);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await SetAsync(false, 0, "", timeout.Token); }
        finally { _gate.Dispose(); }
    }
}
