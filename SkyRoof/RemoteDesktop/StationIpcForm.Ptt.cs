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
            microphoneEnabled, "48 kHz mono desktop mic");
    }

    private async Task BeginPttAsync()
    {
        await pttCommands.WaitAsync();
        try
        {
            if (!pttHeld || pttClosing || pttTransaction is not null) return;
            string transaction = Guid.NewGuid().ToString("N");
            pttTransaction = transaction;
            var snapshot = await client.ManualPttAsync(DeviceOperation.DeviceManualBegin, transaction, lifetime.Token, microphoneEnabled.Checked ? "LAN" : "");
            ShowRadio(snapshot);
            if (pttHeld && client.OwnsPtt(snapshot, transaction))
            {
                ptt.Text = "TX · HOLD";
                heartbeat.Start();
                if (microphoneEnabled.Checked && snapshot.NativeTxAvailable)
                    StartMicrophone(transaction, snapshot.TransactionEpoch);
                else if (microphoneEnabled.Checked)
                    status.Text = "No native TX audio grant; CAT PTT remains active.";
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
            StopMicrophone();
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
        if (IsDisposed || pttCloseComplete) return;
        pttHeld = false;
        StopMicrophone();
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

    private async Task ClosePttAsync(FormClosingEventArgs e)
    {
        if (pttCloseComplete) return;
        e.Cancel = true;
        if (pttClosing) return;
        pttClosing = true;
        polling.Stop();
        digitalCancellation?.Cancel();
        cwSendCancellation?.Cancel();
        StopSignalStreams();
        await ReleasePttAsync();
        await RunAsync(async () => ShowRadio(await client.ReleaseSessionAsync(CancellationToken.None)));
        pttCloseComplete = true;
        BeginInvoke(new Action(Close));
    }
}
