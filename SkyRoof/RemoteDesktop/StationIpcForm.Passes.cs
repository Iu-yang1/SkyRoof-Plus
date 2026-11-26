using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly DateTimePicker passStart = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Width = 175, Value = DateTime.UtcNow };
    private readonly NumericUpDown passHours = new() { Minimum = 1, Maximum = 72, Value = 24, Width = 55 };
    private readonly CheckBox commonMoon = new() { Text = "Common Moon visibility (same mask at both sites)", AutoSize = true };
    private readonly ListView passList = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill };
    private readonly RemoteSkyPlot passPlot = new() { Dock = DockStyle.Fill };
    private readonly Label passStatus = new() { Dock = DockStyle.Bottom, Height = 40, AutoEllipsis = true };
    private CancellationTokenSource? passSearch;

    private TabPage CreatePassesPage()
    {
        var page = new TabPage("PASSES / SKY");
        var toolbar = Row("START UTC", passStart, "Hours", passHours,
            ActionButton("NOW", PassNowAsync), ActionButton("FIND PASSES", FindPassesAsync),
            ActionButton("CANCEL", () => StopStream(passSearch)), commonMoon);
        toolbar.Dock = DockStyle.Top;
        toolbar.WrapContents = true;
        foreach (var column in new[] { "AOS UTC", "LOS UTC", "Peak UTC", "Max EL°", "Backend" })
            passList.Columns.Add(column, 145);
        passList.SelectedIndexChanged += (_, _) =>
        {
            passPlot.ShowPath(passList.SelectedItems.Count == 1 ? passList.SelectedItems[0].Tag as PassWindow : null);
        };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(900, 650), SplitterDistance = 240 };
        split.Panel1.Controls.Add(passList);
        split.Panel2.Controls.Add(passPlot);
        page.Controls.Add(split);
        page.Controls.Add(toolbar);
        page.Controls.Add(passStatus);
        return page;
    }

    private Task PassNowAsync()
    {
        passStart.Value = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    private async Task FindPassesAsync()
    {
        if (passSearch is not null) return;
        var request = ReadTrackingRequest();
        if (commonMoon.Checked && request.EmePeer is null && !advancedTracking.Checked)
            request.EmePeer = new EmePeerSite { LatitudeDegrees = ReadNumber(peerLat), LongitudeDegrees = ReadNumber(peerLon), HeightMetres = ReadNumber(peerHeight) };
        var utc = new DateTimeOffset(DateTime.SpecifyKind(passStart.Value, DateTimeKind.Utc));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        passSearch = source;
        passStatus.Text = "Computing Station pass windows; no hardware writes.";
        try
        {
            var result = await client.PlanPassesAsync(new PassSearchRequest
            {
                Tracking = request, StartUtcMilliseconds = utc.ToUnixTimeMilliseconds(),
                EndUtcMilliseconds = utc.AddHours((double)passHours.Value).ToUnixTimeMilliseconds(),
                CommonMoonVisibility = commonMoon.Checked
            }, source.Token);
            if (!IsDisposed) ShowPasses(result);
        }
        finally { passSearch = null; }
    }

    private void ShowPasses(PassSearchResult result)
    {
        passList.BeginUpdate();
        try
        {
            passList.Items.Clear();
            passPlot.ShowPath(null);
            foreach (var pass in result.Passes)
            {
                string start = UtcLabel(pass.AosUtcMilliseconds) + (pass.ActualAos ? "" : "*");
                string end = UtcLabel(pass.LosUtcMilliseconds) + (pass.ActualLos ? "" : "*");
                string backend = string.Join("/", pass.SkyPath.Select(point => point.OrbitBackend).Distinct());
                var item = new ListViewItem(new[] { start, end, UtcLabel(pass.CulminationUtcMilliseconds), pass.MaximumElevationDegrees.ToString("F2"), backend }) { Tag = pass };
                passList.Items.Add(item);
            }
            passStatus.Text = $"{result.Passes.Count} windows · * search boundary, not horizon crossing" +
                (result.CommonMoonVisibility ? " · max EL is min(local, DX)" : "");
            if (passList.Items.Count > 0) passList.Items[0].Selected = true;
        }
        finally { passList.EndUpdate(); }
    }

    private static string UtcLabel(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToString("MM-dd HH:mm:ss");
}
