using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly TextBox autoHost = new() { Width = 230, Text = "http://127.0.0.1:8076" };
    private readonly TextBox autoToken = new() { Width = 230, UseSystemPasswordChar = true };
    private readonly TextBox autoCall = new() { Width = 95 };
    private readonly TextBox autoGrid = new() { Width = 75 };
    private readonly TextBox autoTarget = new() { Width = 95 };
    private readonly NumericUpDown autoAttempts = new() { Minimum = 1, Maximum = int.MaxValue, Value = 4, Width = 65 };
    private readonly TextBox autoFrequency = new() { Width = 120 };
    private readonly TextBox autoTrackingJob = new() { Width = 230 };
    private readonly ComboBox autoMode = new() { Width = 75, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label autoStatus = new() { AutoSize = true, MaximumSize = new Size(850, 0) };
    private readonly TextBox autoEvents = new() { Multiline = true, ReadOnly = true, Width = 790, Height = 240, ScrollBars = ScrollBars.Vertical };
    private DesktopAutoSession? autoConnection;
    private string autoStation = "";
    private bool connectingAuto;

    private TabPage CreateAutoPage()
    {
        var page = new TabPage("AUTO QSO");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        autoMode.Items.AddRange(new object[] { "FT4", "Q65" });
        autoMode.SelectedIndex = 0;
        layout.Controls.Add(Row("Host origin", autoHost, "Access token", autoToken, ActionButton("CONNECT", ConnectAutoAsync)));
        layout.Controls.Add(Row(autoMode, "MY CALL", autoCall, "GRID", autoGrid, "DX (blank = CQ)", autoTarget, "Attempts", autoAttempts));
        layout.Controls.Add(Row("Host TX Hz", autoFrequency, ActionButton("USE CAT TX", CopyCatTxAsync), "Q65 Moon job ID", autoTrackingJob, ActionButton("USE MOON JOB", UseMoonJobAsync)));
        layout.Controls.Add(Row(ActionButton("START AUTO QSO", StartAutoAsync), ActionButton("STOP AUTO QSO", StopAutoAsync)));
        layout.Controls.Add(autoStatus);
        layout.Controls.Add(autoEvents);
        layout.Controls.Add(Row("AF/gain/parity and Q65 RX profile use DIGITAL / TELEMETRY settings."));
        page.Controls.Add(layout);
        return page;
    }

    private async Task ConnectAutoAsync()
    {
        if (digitalCancellation is not null || connectingAuto) return;
        connectingAuto = true;
        try { await ReplaceAutoConnectionAsync(); }
        finally { connectingAuto = false; }
    }

    private async Task ReplaceAutoConnectionAsync()
    {
        autoConnection?.Dispose();
        autoConnection = null;
        autoStation = "";
        var endpoint = new Uri(autoHost.Text.Trim());
        var connection = new DesktopAutoSession(endpoint, autoToken.Text.Trim());
        autoToken.Clear();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var identity = await connection.StationAsync(timeout.Token);
            var local = await client.IdentifyAsync(timeout.Token);
            var remote = await connection.RuntimeInstanceAsync(timeout.Token);
            if (!string.Equals(local.InstanceId, remote, StringComparison.Ordinal))
                throw new InvalidOperationException("Host does not control the selected local IPC Station.");
            autoStation = $"{identity} · {endpoint.GetLeftPart(UriPartial.Authority)}";
            if (pttClosing || IsDisposed) { connection.Dispose(); return; }
            autoConnection = connection;
            autoStatus.Text = "Authenticated Host: " + autoStation;
        }
        catch { connection.Dispose(); throw; }
    }

    private async Task UseMoonJobAsync()
    {
        var job = await client.TrackingAsync(lifetime.Token);
        if (!job.Running || job.Latest?.CelestialTarget != "MOON" || job.Latest.TxHz <= 0)
            throw new InvalidOperationException("Start a Moon tracking job with TX frequency first.");
        autoTrackingJob.Text = job.JobId;
    }

    private async Task CopyCatTxAsync()
    {
        var snapshot = await client.ReadRadioAsync(lifetime.Token);
        autoFrequency.Text = snapshot.TxFrequencyHz.ToString(CultureInfo.InvariantCulture);
    }

    private async Task StartAutoAsync()
    {
        if (digitalCancellation is not null || pttClosing) return;
        var connection = autoConnection ?? throw new InvalidOperationException("Connect to the Host first.");
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        digitalCancellation = source;
        autoEvents.Clear();
        string mode = autoMode.Text;
        try
        {
            var local = await client.IdentifyAsync(source.Token);
            await connection.StartAsync(mode, BuildAutoRequest(mode), source.Token, local.InstanceId);
            while (await connection.ReadAsync(source.Token) is { } evt)
                ShowAutoEvent(evt);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        { if (!IsDisposed) autoStatus.Text = "Cancellation requested; verify CAT RX."; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException)
        { if (!IsDisposed) autoStatus.Text = error.Message; }
        finally
        {
            digitalCancellation = null;
            connection.End();
        }
    }

    private object BuildAutoRequest(string mode)
    {
        var job = mode == "Q65" ? autoTrackingJob.Text.Trim() : "";
        long hz = job.Length > 0 ? 0 : long.Parse(autoFrequency.Text, CultureInfo.InvariantCulture);
        float gain = float.Parse(digitalGain.Text, CultureInfo.InvariantCulture);
        float af = float.Parse(digitalAudioHz.Text, CultureInfo.InvariantCulture);
        var body = new Dictionary<string, object?>
        {
            ["call"] = autoCall.Text.Trim(), ["grid"] = autoGrid.Text.Trim(), ["target"] = autoTarget.Text.Trim(),
            ["frequencyHz"] = hz, ["gain"] = gain, ["odd"] = digitalOdd.Checked, ["attempts"] = (int)autoAttempts.Value
        };
        if (mode == "FT4") body["audioHz"] = af;
        else
        {
            var config = JsonParser.Default.Parse<Q65RxConfiguration>(q65Config.Text);
            config.Submode = q65Submode.Text.Trim();
            config.RxHz = af;
            body["rx"] = config;
            body["trackingJobId"] = job.Length > 0 ? job : null;
        }
        return body;
    }

    private void ShowAutoEvent(JsonElement evt)
    {
        if (IsDisposed) return;
        autoStatus.Text = autoStation + " · " + evt.GetProperty("state").GetString();
        if (autoEvents.TextLength > 100000) autoEvents.Clear();
        autoEvents.AppendText(evt.GetRawText() + Environment.NewLine);
    }

    private Task StopAutoAsync()
    {
        digitalCancellation?.Cancel();
        return Task.CompletedTask;
    }
}
