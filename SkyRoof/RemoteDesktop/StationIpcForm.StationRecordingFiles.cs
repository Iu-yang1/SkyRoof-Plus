using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly ListView stationFiles = new() { Width = 750, Height = 180, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private string stationFileCursor = "";
    private CancellationTokenSource? stationDownload;
    private bool stationFilesBusy;

    private void AddStationRecordingControls(FlowLayoutPanel layout)
    {
        stationFiles.Columns.Add("Station WAV", 400);
        stationFiles.Columns.Add("MiB", 90);
        stationFiles.Columns.Add("State", 220);
        layout.Controls.Add(Row(ActionButton("REFRESH STATION FILES", () => ListStationFilesAsync(true)),
            ActionButton("MORE FILES", () => ListStationFilesAsync(false)),
            ActionButton("DOWNLOAD WAV", DownloadStationFileAsync),
            ActionButton("CANCEL DOWNLOAD", () => StopStream(stationDownload)),
            ActionButton("DELETE STATION FILE", DeleteStationFileAsync)));
        layout.Controls.Add(stationFiles);
    }

    private async Task ListStationFilesAsync(bool reset)
    {
        if (stationFilesBusy || (!reset && stationFileCursor.Length == 0)) return;
        stationFilesBusy = true;
        try
        {
            var result = await client.ListStationRecordingsAsync(reset ? "" : stationFileCursor, lifetime.Token);
            if (IsDisposed) return;
            if (reset) stationFiles.Items.Clear();
            foreach (var file in result.Files)
                stationFiles.Items.Add(new ListViewItem(new[] { file.Name, (file.SizeBytes / 1048576.0).ToString("F1"), file.Detail }) { Tag = file });
            stationFileCursor = result.NextBeforeName;
        }
        finally { stationFilesBusy = false; }
    }

    private StationRecordingFile SelectedStationFile() =>
        stationFiles.SelectedItems.Count == 1 && stationFiles.SelectedItems[0].Tag is StationRecordingFile file
            ? file : throw new InvalidOperationException("Select a station recording.");

    private async Task DownloadStationFileAsync()
    {
        if (stationDownload is not null) return;
        var file = SelectedStationFile();
        if (!file.Finalized) throw new InvalidOperationException("Refresh after the WAV segment has closed.");
        using var dialog = new SaveFileDialog { Filter = "Waveform|*.wav", FileName = file.Name };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string destination = dialog.FileName, temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        stationDownload = source;
        var progress = new Progress<long>(bytes => { if (!IsDisposed) stationRecordStatus.Text = $"Downloading {bytes / 1048576.0:F1} MiB"; });
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 262144, true))
            {
                await client.CopyStationRecordingAsync(file.Name, output, progress, source.Token);
                await output.FlushAsync(source.Token);
            }
            File.Move(temp, destination, true);
            if (!IsDisposed)
            {
                recordFiles.Items.Add(destination);
                recordFiles.SelectedIndex = recordFiles.Items.Count - 1;
                stationRecordStatus.Text = "Downloaded " + file.Name;
            }
        }
        finally
        {
            stationDownload = null;
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private async Task DeleteStationFileAsync()
    {
        if (stationDownload is not null || stationFilesBusy) return;
        var file = SelectedStationFile();
        if (MessageBox.Show(this, "Permanently delete the station recording " + file.Name + "?",
            "Station recorder", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        var deleted = await client.DeleteStationRecordingAsync(file.Name, lifetime.Token);
        stationRecordStatus.Text = "Deleted " + deleted.Name;
        await ListStationFilesAsync(true);
    }
}
