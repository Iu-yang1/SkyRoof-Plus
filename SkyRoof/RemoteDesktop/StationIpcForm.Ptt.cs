using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly System.Windows.Forms.Timer heartbeat = new() { Interval = 1000 };
    private readonly Button ptt = new() { Text = "HOLD PTT", AutoSize = true };
    private readonly SemaphoreSlim pttCommands = new(1, 1);
    private string? pttTransaction;
    private bool pttHeld;
    private bool pttClosing;
    private bool pttCloseComplete;
    private bool heartbeatPending;

    private Control CreatePttRow()
    {
        ptt.MouseDown += async (_, e) =>
        {
            if (e.Button != MouseButtons.Left || pttHeld || pttClosing) return;
            pttHeld = true;
            ptt.Capture = true;
            await RunAsync(BeginPttAsync);
        };
        ptt.MouseUp += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left) await ReleasePttAsync();
        };
        ptt.MouseCaptureChanged += async (_, _) =>
        {
            if (!ptt.Capture) await ReleasePttAsync();
        };
        heartbeat.Tick += async (_, _) => await RunAsync(HeartbeatAsync);
        return Row(ptt, ActionButton("RX", ReleasePttAsync),
            "CAT PTT · station audio input");
    }

    private async Task BeginPttAsync()
    {
        await pttCommands.WaitAsync();
        try
        {
            if (!pttHeld || pttClosing || pttTransaction is not null) return;
            string transaction = Guid.NewGuid().ToString("N");
            pttTransaction = transaction;
            var snapshot = await client.ManualPttAsync(DeviceOperation.DeviceManualBegin, transaction, lifetime.Token);
            ShowRadio(snapshot);
            if (pttHeld && client.OwnsPtt(snapshot, transaction))
            {
                ptt.Text = "TX · HOLD";
                heartbeat.Start();
            }
        }
        finally { pttCommands.Release(); }
    }

    private async Task HeartbeatAsync()
    {
        if (heartbeatPending || !pttHeld || pttClosing) return;
        heartbeatPending = true;
        await pttCommands.WaitAsync();
        try
        {
            if (!pttHeld || pttTransaction is not { } transaction) return;
            var snapshot = await client.ManualPttAsync(DeviceOperation.DeviceManualHeartbeat, transaction, lifetime.Token);
            ShowRadio(snapshot);
            if (client.OwnsPtt(snapshot, transaction)) return;
            pttTransaction = null;
            pttHeld = false;
            heartbeat.Stop();
            ptt.Text = "HOLD PTT";
        }
        finally
        {
            pttCommands.Release();
            heartbeatPending = false;
        }
    }

    private async Task ReleasePttAsync()
    {
        pttHeld = false;
        heartbeat.Stop();
        ptt.Text = "HOLD PTT";
        if (ptt.Capture) ptt.Capture = false;
        await pttCommands.WaitAsync();
        try
        {
            if (pttTransaction is not { } transaction) return;
            await RunAsync(async () =>
            {
                var snapshot = await client.ManualPttAsync(DeviceOperation.DeviceManualEnd, transaction, CancellationToken.None);
                pttTransaction = null;
                ShowRadio(snapshot);
            });
        }
        finally { pttCommands.Release(); }
    }

    private async void ClosePttAsync(object? sender, FormClosingEventArgs e)
    {
        if (pttCloseComplete) return;
        e.Cancel = true;
        if (pttClosing) return;
        pttClosing = true;
        polling.Stop();
        await ReleasePttAsync();
        await RunAsync(async () => ShowRadio(await client.ReleaseSessionAsync(CancellationToken.None)));
        pttCloseComplete = true;
        Close();
    }
}
