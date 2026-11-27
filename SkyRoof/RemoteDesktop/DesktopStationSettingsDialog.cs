using System.Diagnostics;

namespace SkyRoof.RemoteDesktop;

internal sealed class DesktopStationSettingsDialog : Form
{
    private readonly DesktopStationSettings profile;
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox station = new() { Width = 260 };
    private readonly TextBox pipe = new() { Width = 360 };
    private readonly TextBox host = new() { Width = 360 };
    private readonly TextBox config = new() { Width = 420 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly bool serverWorkspace;
    private bool busy;

    internal DesktopStationSettingsDialog(bool serverWorkspace, string? recoveryError = null)
    {
        this.serverWorkspace = serverWorkspace;
        profile = recoveryError is null ? DesktopStationSettings.Load() : new DesktopStationSettings();
        if (recoveryError is not null) status.Text = "Recover startup configuration: " + recoveryError + ". Save corrected settings, then reopen SkyRoof.";
        Text = "SkyRoof · Remote / Server Settings";
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(650, 490);
        StartPosition = FormStartPosition.CenterParent;
        mode.Items.AddRange(new object[] { "Standalone (local radio)", "Server workspace (local Station IPC)" });
        mode.SelectedIndex = (int)profile.Mode;
        station.Text = profile.StationId;
        pipe.Text = profile.StationPipe;
        host.Text = profile.HostOrigin;
        config.Text = profile.ConfigurationFile;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        layout.Controls.Add(Row("Startup mode", mode));
        layout.Controls.Add(Row("Station ID", station, Button("DEFAULT PIPE", () => { pipe.Text = "skyroof-" + station.Text.Trim().ToLowerInvariant() + "-station"; return Task.CompletedTask; })));
        layout.Controls.Add(Row("Local IPC pipe", pipe));
        layout.Controls.Add(Row("Host origin", host, Button("OPEN WEB", OpenWebAsync)));
        layout.Controls.Add(Row("Supervisor JSON", config, Button("BROWSE", BrowseAsync)));
        layout.Controls.Add(Row(Button("EDIT CONFIGURATION", EditConfigAsync), Button("TEST IPC", TestAsync)));
        layout.Controls.Add(Row(Button("SERVICE STATUS", () => ServiceAsync("query")),
            Button("START SERVICE", () => ServiceAsync("start"), serverWorkspace),
            Button("STOP SERVICE", () => ServiceAsync("stop"), serverWorkspace)));
        layout.Controls.Add(Row(Button("SAVE", SaveAsync), Button("CLOSE", () => { Close(); return Task.CompletedTask; })));
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(660, 0),
            Text = "Mode and connection changes apply on next SkyRoof launch. Server mode never starts local CAT/SDR. Install the station package first; service commands require Windows administrator permission. Stop the station service before returning to standalone hardware control." });
        layout.Controls.Add(status);
        Controls.Add(layout);
    }

    private static FlowLayoutPanel Row(params object[] items)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        foreach (var item in items)
            row.Controls.Add(item is Control c ? c : new Label { Text = item.ToString(), AutoSize = true });
        return row;
    }

    private Button Button(string text, Func<Task> action, bool enabled = true)
    {
        var button = new Button { Text = text, AutoSize = true, Enabled = enabled };
        button.Click += async (_, _) => await RunAsync(action);
        return button;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try { await action(); }
        catch (Exception error) { if (!IsDisposed) status.Text = error.Message; }
        finally { busy = false; }
    }

    private DesktopStationSettings ReadFields()
    {
        var value = new DesktopStationSettings { Mode = (DesktopStationMode)mode.SelectedIndex,
            StationId = station.Text.Trim().ToLowerInvariant(), StationPipe = pipe.Text.Trim(),
            HostOrigin = host.Text.Trim(), ConfigurationFile = config.Text.Trim() };
        value.Validate();
        return value;
    }

    private Task SaveAsync()
    {
        ReadFields().Save();
        status.Text = "Saved. Close SkyRoof normally and reopen it to apply startup mode and connection changes.";
        return Task.CompletedTask;
    }

    private Task BrowseAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Supervisor configuration|*.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) config.Text = dialog.FileName;
        return Task.CompletedTask;
    }

    private Task EditConfigAsync()
    {
        var current = ReadFields();
        using var editor = new StationConfigurationEditor(current.ConfigurationFile, current.StationPipe);
        editor.ShowDialog(this);
        return Task.CompletedTask;
    }

    private Task OpenWebAsync()
    {
        Process.Start(new ProcessStartInfo(ReadFields().HostOrigin) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private async Task TestAsync()
    {
        using var client = new StationIpcClient(ReadFields().StationPipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var identity = await client.IdentifyAsync(timeout.Token);
        status.Text = identity.StationId == ReadFields().StationId
            ? $"Station {identity.StationId} connected · instance {identity.InstanceId}"
            : $"Connected to {identity.StationId}, not selected station {ReadFields().StationId}. Check pipe configuration.";
    }

    private async Task ServiceAsync(string action)
    {
        if (action != "query" && !serverWorkspace)
            throw new InvalidOperationException("Switch to server workspace before changing service state.");
        var settings = ReadFields();
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(settings.ServiceName);
        using var process = Process.Start(start) ?? throw new IOException("Unable to run Windows service control.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        status.Text = (await output + " " + await error).Trim();
        if (process.ExitCode != 0) status.Text = $"SC exit {process.ExitCode}: " + status.Text;
    }
}
