using System.ComponentModel;
using Serilog;

namespace SkyRoof
{
  public partial class DownloadDialog : Form
  {
    private Context ctx = null!;
    private SatnogsDb? db;
    public DownloadDialog()
    {
      InitializeComponent();

      // The unfilled part of the bar paints the control's own BackColor, which inherits the form
      // and so is invisible against it in the dark theme: a bar at 0% shows nothing at all.
      // Unconditional - under visual styles the light theme draws its own trough and ignores
      // BackColor, so this changes nothing there.
      progressBar1.BackColor = SystemColors.ControlDark;

      // Keep the form open until the async operation has actually stopped.
      // This avoids disposing controls while a cancellation is still unwinding.
      Button.DialogResult = DialogResult.None;
    }

    public static bool Download(Form parent, Context ctx)
    {
      using var dlg = new DownloadDialog
      {
        ctx = ctx
      };

      var rc = dlg.ShowDialog(parent);
      return rc == DialogResult.OK;
    }

    private void Button_Click(object sender, EventArgs e)
    {
      if (Button.Text == "Close")
      {
        DialogResult = DialogResult.Cancel;
        Close();
        return;
      }

      Button.Enabled = false;
      Button.Text = "Cancelling...";

      db?.AbortDownload();
    }

    private async void DownloadDialog_Shown(
      object sender,
      EventArgs e)
    {
      await DownloadSatelliteDataAsync();
    }

    private async Task DownloadSatelliteDataAsync()
    {
      // Construct the staging database away from WinForms as well: loading
      // override files in its constructor can involve disk I/O.
      var sources = ctx.Settings.OrbitSources;
      var groupIds = ctx.Settings.Satellites.SatelliteGroups
        .SelectMany(group => group.SatelliteIds).Distinct().ToArray();
      db = await Task.Run(() =>
      {
        var staging = new SatnogsDb();
        staging.ConfigureSources(sources);
        return staging;
      });
      db.DownloadProgress += SatnogsDb_DownloadProgress;

      // download files
      try
      {
        await Task.Run(() => db.DownloadAll());
      }
      catch (OperationCanceledException)
      {
        DialogResult = DialogResult.Cancel;
        return;
      }
      catch (Exception ex)
      {
        ErrorLabel.Text = "Download Failed";
        Button.Text = "Close";
        Log.Error(ex, ErrorLabel.Text);
        db = null;
        DialogResult = DialogResult.None;
        return;
      }

      // import files
      try
      {
        // Parsing the satellite JSON, merging transmitter overrides, and
        // writing the imported cache are CPU/disk tasks, never UI work.
        await Task.Run(db.ImportAll);

        // Read the live operator's manual orbit overrides only on the UI
        // thread. The staged database itself remains private to this dialog.
        db.CopyActiveManualOrbitOverridesFrom(
          ctx.SatnogsDb,
          DateTime.UtcNow);

        await Task.Run(() => db.LoadAutomaticOrbitOverlaysAsync());
        await Task.Run(() =>
        {
          db.ConfigureSolarSystem(sources);
          foreach (string id in groupIds)
            _ = db.GetSatellite(id)?.Tracker.Enabled;
        });

        // The one atomic UI handoff preserves selection/CAT ownership.
        ctx.Settings.Satellites.EnsureSolarSystemGroup(db);
        ctx.SatnogsDb.ReplaceSatelliteList(db);
        DialogResult = DialogResult.OK;
      }
      catch (Exception ex)
      {
        ErrorLabel.Text = "Data Import Failed";
        Button.Text = "Close";
        Log.Error(ex, ErrorLabel.Text);
      }
    }

    private void SatnogsDb_DownloadProgress(
      object? sender,
      ProgressChangedEventArgs e)
    {
      if (InvokeRequired)
      {
        BeginInvoke(() => SatnogsDb_DownloadProgress(sender, e));
        return;
      }
      if (!IsDisposed && !Disposing)
        progressBar1.Value = Math.Clamp(e.ProgressPercentage, 0, 100);
    }
  }
}
