using System.Diagnostics;
using System.Text.Json;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private bool externalLoggerInitialized;
    private readonly TextBox tqslExecutable = new() { Width = 230 };
    private readonly TextBox tqslLocation = new() { Width = 135 };
    private bool tqslBusy;

    private void AddExternalLogControls(FlowLayoutPanel fields)
    {
        fields.Controls.Add(Row(ActionButton("LOOKUP EXTERNAL LOGGER", ExternalLookupAsync),
            ActionButton("SEND SELECTED QSO TO LOGGER", ExternalSendAsync)));
        fields.Controls.Add(Row("TQSL", tqslExecutable, ActionButton("BROWSE TQSL", BrowseTqslAsync),
            "Station location", tqslLocation, ActionButton("UPLOAD FILTERED LOG TO LOTW", UploadLotwAsync)));
    }

    private void InitializeExternalLogger()
    {
        if (externalLoggerInitialized) return;
        LoggerInterfaceDll.Init();
        externalLoggerInitialized = true;
    }

    private static QsoInfo LegacyQso(QsoRecord qso) => new()
    {
        StationCallsign = qso.StationCall, MyGridSquare = qso.MyGrid,
        Utc = DateTimeOffset.FromUnixTimeMilliseconds(qso.UtcMilliseconds).UtcDateTime,
        Call = qso.Call, Band = qso.Band, Mode = qso.Mode, Sat = qso.Satellite,
        Grid = qso.Grid, State = qso.State, Sent = qso.Sent, Recv = qso.Received,
        Name = qso.Name, Notes = qso.Notes
    };

    private Task ExternalLookupAsync()
    {
        try
        {
            InitializeExternalLogger();
            var info = LegacyQso(BuildQso());
            if (info.Call.Length < 3) throw new InvalidOperationException("Enter the DX callsign.");
            var result = JsonSerializer.Deserialize<QsoInfo>(LoggerInterfaceDll.Augment(JsonSerializer.Serialize(info)));
            if (result is null) throw new InvalidDataException("External logger returned no lookup.");
            foreach (var (name, value) in new[] { ("GRID", result.Grid), ("NAME", result.Name), ("STATE", result.State) })
                if (QsoField(name).Length == 0) qsoFields[name].Text = value;
            qsoStatus.Text = "External logger lookup · " + result.StatusString;
        }
        catch (Exception error) { qsoStatus.Text = "External logger lookup failed: " + error.Message; }
        return Task.CompletedTask;
    }

    private Task ExternalSendAsync()
    {
        if (qsoList.SelectedItems.Count != 1 || qsoList.SelectedItems[0].Tag is not QsoRecord saved)
            throw new InvalidOperationException("Search log and select one saved QSO.");
        if (MessageBox.Show(this, "Send the selected saved record to the installed external logger? Verify duplicates and submode in that logger.",
            "External logger", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return Task.CompletedTask;
        try
        {
            InitializeExternalLogger();
            var legacy = LegacyQso(saved);
            // The original DLL ABI understands QsoInfo. New fields are supplied
            // as additional JSON properties; legacy plugins may ignore them.
            var payload = JsonSerializer.SerializeToNode(legacy)?.AsObject() ?? throw new InvalidDataException("Cannot serialize QSO.");
            payload["Submode"] = saved.Submode;
            payload["TxFreqHz"] = saved.TxHz;
            payload["RxFreqHz"] = saved.RxHz;
            string result = LoggerInterfaceDll.SaveQso(payload.ToJsonString());
            qsoStatus.Text = string.IsNullOrEmpty(result) ? "External logger accepted; verify submode and fields." : result;
        }
        catch (Exception error) { qsoStatus.Text = "External logger result not confirmed: " + error.Message; }
        return Task.CompletedTask;
    }

    private Task BrowseTqslAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "TrustedQSL executable|tqsl.exe|Executable|*.exe" };
        if (dialog.ShowDialog(this) == DialogResult.OK) tqslExecutable.Text = dialog.FileName;
        return Task.CompletedTask;
    }

    private async Task UploadLotwAsync()
    {
        if (tqslBusy || qsoBusy) return;
        string executable = tqslExecutable.Text.Trim();
        string stationLocation = tqslLocation.Text.Trim();
        if (!File.Exists(executable)) throw new InvalidOperationException("Select installed TrustedQSL (tqsl.exe).");
        if (stationLocation.Length == 0) throw new InvalidOperationException("Enter the configured TQSL Station Location.");
        if (MessageBox.Show(this, "Export and upload the Station log matching the current call filter using the selected TQSL location? TQSL handles certificate and duplicates.",
            "LoTW", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        tqslBusy = qsoBusy = true;
        string root = Path.Combine(Path.GetTempPath(), "SkyRoof-LoTW-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "station-log.adi");
        try
        {
            int count = await WriteAdifAsync(path, qsoFilter.Text.Trim());
            if (count == 0) { qsoStatus.Text = "No matching QSOs."; return; }
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-u", "-a", "compliant", "-x", "-l", stationLocation, path })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Unable to start TrustedQSL.");
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            string report = (await error + Environment.NewLine + await output).Trim();
            if (!IsDisposed)
            {
                qsoStatus.Text = process.ExitCode switch
                {
                    0 => "TQSL reported upload success; LoTW processing/confirmation is separate.",
                    8 => "TQSL processed no records (duplicates or date range).",
                    9 => "TQSL reported partial upload; inspect the result.",
                    _ => $"TQSL exit {process.ExitCode}; upload not confirmed."
                };
                MessageBox.Show(this, report.Length == 0 ? qsoStatus.Text : report, "TrustedQSL result");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            tqslBusy = qsoBusy = false;
        }
    }
}
