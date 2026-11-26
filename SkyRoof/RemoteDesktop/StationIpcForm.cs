using System.Globalization;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm : Form
{
    private readonly StationIpcClient client;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer polling = new() { Interval = 1200 };
    private readonly Label radio = new() { AutoSize = true, Text = "Connecting CAT..." };
    private readonly Label rotator = new() { AutoSize = true, Text = "Connecting rotator..." };
    private readonly Label tracking = new() { AutoSize = true, Text = "Connecting tracking..." };
    private readonly Label status = new() { AutoSize = true };
    private readonly TextBox rx = new() { Width = 120 };
    private readonly TextBox tx = new() { Width = 120 };
    private readonly TextBox mode = new() { Width = 75, Text = "USB" };
    private readonly TextBox az = new() { Width = 75 };
    private readonly TextBox el = new() { Width = 75 };
    private bool refreshing;

    internal StationIpcForm(string pipe)
    {
        client = new(pipe);
        Text = "SkyRoof Remote · " + pipe;
        ClientSize = new Size(680, 350);
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12)
        };
        layout.Controls.Add(radio);
        layout.Controls.Add(Row("RX Hz", rx, ActionButton("SET RX", () => TuneAsync(DeviceOperation.DeviceRxFrequency, rx))));
        layout.Controls.Add(Row("TX Hz", tx, ActionButton("SET TX", () => TuneAsync(DeviceOperation.DeviceTxFrequency, tx))));
        layout.Controls.Add(Row("Mode", mode,
            ActionButton("RX MODE", () => ModeAsync(DeviceOperation.DeviceRxMode)),
            ActionButton("TX MODE", () => ModeAsync(DeviceOperation.DeviceTxMode))));
        layout.Controls.Add(CreatePttRow());
        layout.Controls.Add(rotator);
        layout.Controls.Add(Row("AZ", az, "EL", el,
            ActionButton("POINT", PointAsync), ActionButton("STOP", StopAsync)));
        layout.Controls.Add(tracking);
        layout.Controls.Add(status);
        Controls.Add(layout);
        polling.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { polling.Start(); await RefreshAsync(); };
        FormClosing += ClosePttAsync;
        Deactivate += async (_, _) => await ReleasePttAsync();
        FormClosed += (_, _) =>
        {
            heartbeat.Dispose();
            polling.Dispose();
            lifetime.Cancel();
            client.Dispose();
            lifetime.Dispose();
        };
    }

    private static FlowLayoutPanel Row(params object[] items)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        foreach (var item in items)
            row.Controls.Add(item is Control control ? control : new Label { Text = item.ToString(), AutoSize = true });
        return row;
    }

    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) => await RunAsync(action);
        return button;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is RpcException or IOException or FormatException or OverflowException or OperationCanceledException or ObjectDisposedException)
        {
            if (!IsDisposed) status.Text = error.Message;
        }
    }

    private async Task RefreshAsync()
    {
        if (refreshing || IsDisposed) return;
        refreshing = true;
        try
        {
            await RunAsync(async () => ShowRadio(await client.ReadRadioAsync(lifetime.Token)));
            await RunAsync(async () => ShowRotator(await client.ReadRotatorAsync(lifetime.Token)));
            await RunAsync(async () =>
            {
                var job = await client.TrackingAsync(lifetime.Token);
                if (!IsDisposed) tracking.Text = $"TRACK · {job.Phase} · {job.CompletedTicks} ticks";
            });
        }
        finally { refreshing = false; }
    }

    private async Task TuneAsync(DeviceOperation operation, TextBox value)
    {
        long hz = long.Parse(value.Text, CultureInfo.InvariantCulture);
        ShowRadio(await client.SetRadioAsync(operation, hz, "", lifetime.Token));
    }

    private async Task ModeAsync(DeviceOperation operation) =>
        ShowRadio(await client.SetRadioAsync(operation, 0, mode.Text.Trim(), lifetime.Token));

    private async Task PointAsync() =>
        ShowRotator(await client.PointAsync(double.Parse(az.Text, CultureInfo.InvariantCulture),
            double.Parse(el.Text, CultureInfo.InvariantCulture), lifetime.Token));

    private async Task StopAsync() => ShowRotator(await client.StopAsync(lifetime.Token));

    private void ShowRadio(DeviceSnapshot snapshot)
    {
        if (IsDisposed) return;
        radio.Text = $"RX {snapshot.RxFrequencyHz:N0} Hz {snapshot.RxMode} · TX {snapshot.TxFrequencyHz:N0} Hz {snapshot.TxMode} · {(snapshot.ActualPtt ? "PTT" : "RX")}";
        if (rx.Text.Length == 0) rx.Text = snapshot.RxFrequencyHz.ToString(CultureInfo.InvariantCulture);
        if (tx.Text.Length == 0) tx.Text = snapshot.TxFrequencyHz.ToString(CultureInfo.InvariantCulture);
        status.Text = snapshot.StatusText;
    }

    private void ShowRotator(RotatorPosition position)
    {
        if (IsDisposed) return;
        rotator.Text = position.HasPosition
            ? $"ROT · AZ {position.AzimuthDegrees:F2}° EL {position.ElevationDegrees:F2}° · {(position.ReadbackFresh ? "fresh" : "stale")}"
            : "ROT · no position";
        if (position.StopTransmitted) status.Text = "STOP transmitted; physical stop not independently confirmed.";
    }
}
