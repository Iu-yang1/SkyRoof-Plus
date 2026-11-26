namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private async Task ImportQsoAsync()
    {
        if (qsoBusy) return;
        using var dialog = new OpenFileDialog { Filter = "ADIF|*.adi;*.adif|All files|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        qsoBusy = true;
        int committed = 0;
        try
        {
            string text = await File.ReadAllTextAsync(dialog.FileName, lifetime.Token);
            var records = StationIpcClient.ParseAdif(text, QsoField("MY CALL"));
            qsoStatus.Text = $"Importing {records.Count} QSO records…";
            await client.ImportAdifAsync(records, count =>
            {
                committed = count;
                qsoStatus.Text = $"Import confirmed {count}/{records.Count}";
            }, lifetime.Token);
            qsoStatus.Text = $"Import confirmed {committed}/{records.Count} (including existing identical records).";
        }
        catch (Exception)
        {
            qsoStatus.Text = $"Import interrupted after {committed} confirmed records. Retrying the same file is idempotent.";
            throw;
        }
        finally { qsoBusy = false; }
    }
}
