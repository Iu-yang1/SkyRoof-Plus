using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly RemoteEarthMap earthMap = new() { Dock = DockStyle.Fill };
    private readonly Label earthStatus = new() { Dock = DockStyle.Bottom, Height = 30 };
    private readonly CheckBox earthLive = new() { Text = "Live (selected target)", AutoSize = true };
    private readonly NumericUpDown earthMinutes = new() { Minimum = 1, Maximum = 1440, Value = 100, Width = 65 };
    private readonly CheckBox earthFootprint = new() { Text = "Horizon footprint", Checked = true, AutoSize = true };
    private readonly CheckBox earthGpuEnabled = new() { Text = "GPU / online map", AutoSize = true };
    private readonly ComboBox earthProvider = new() { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label earthMapStatus = new() { Dock = DockStyle.Bottom, Height = 23 };
    private RemoteGpuEarthMap? earthGpu;
    private EarthMapSettings? earthSettings;
    private GroundTrackResult? earthFull, earthCurrent;
    private long earthUtc;
    private bool earthBusy;
    private DateTimeOffset earthLastTrack;
    private TrackingRequest? earthLastRequest;

    private TabPage CreateEarthPage()
    {
        var page = new TabPage("EARTH VIEW");
        earthFootprint.CheckedChanged += (_, _) =>
        {
            earthMap.ShowFootprint = earthFootprint.Checked;
            if (earthGpu is not null) earthGpu.ShowFootprint = earthFootprint.Checked;
        };
        earthProvider.Items.AddRange(Enum.GetNames<EarthMapSourceKind>());
        earthProvider.SelectedIndex = 0;
        earthProvider.SelectedIndexChanged += (_, _) =>
        {
            if (earthSettings is null || earthGpu is null) return;
            earthSettings.Source = Enum.Parse<EarthMapSourceKind>(earthProvider.Text);
            earthGpu.Configure(earthSettings);
        };
        earthGpuEnabled.CheckedChanged += (_, _) => ToggleGpuEarth();
        var row = Row("Past / future minutes", earthMinutes, ActionButton("GROUND TRACK", RefreshEarthTrackAsync), earthLive,
            earthFootprint, ActionButton("RESET MAP", () => { earthMap.ResetView(); earthGpu?.ResetView(); return Task.CompletedTask; }));
        var mapOptions = Row(earthGpuEnabled, "Map provider", earthProvider);
        mapOptions.Dock = DockStyle.Top;
        row.Dock = DockStyle.Top;
        page.Controls.Add(earthMap);
        page.Controls.Add(mapOptions);
        page.Controls.Add(row);
        page.Controls.Add(earthMapStatus);
        page.Controls.Add(earthStatus);
        return page;
    }

    private void ToggleGpuEarth()
    {
        // RemoteDockPane transfers the tab's controls and disposes its original TabPage.
        if (earthMap.Parent is not Control page || page.IsDisposed) return;
        if (!earthGpuEnabled.Checked)
        {
            if (earthGpu is not null) { page.Controls.Remove(earthGpu); earthGpu.Dispose(); earthGpu = null; }
            earthMap.Visible = true;
            earthMapStatus.Text = "Built-in software map";
            return;
        }
        try
        {
            if (earthSettings is null)
            {
                var settings = new Settings();
                settings.LoadFromFile();
                earthSettings = settings.EarthMap;
                earthProvider.SelectedItem = earthSettings.Source.ToString();
            }
            var gpu = new RemoteGpuEarthMap(earthSettings) { Dock = DockStyle.Fill, ShowFootprint = earthFootprint.Checked };
            earthGpu = gpu;
            gpu.MapStatus += detail => { if (!IsDisposed) earthMapStatus.Text = detail; };
            gpu.RendererFailed += detail =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() => { earthGpuEnabled.Checked = false; earthMapStatus.Text = "GPU fallback: " + detail; }));
            };
            page.Controls.Add(gpu);
            page.Controls.SetChildIndex(gpu, 0);
            earthMap.Visible = false;
            if (earthFull is not null) gpu.SetTrack(earthFull, earthUtc, true);
            if (earthCurrent is not null) gpu.SetTrack(earthCurrent, earthUtc, false);
        }
        catch (Exception error)
        {
            earthGpuEnabled.Checked = false;
            earthMapStatus.Text = "GPU fallback: " + error.Message;
        }
    }

    private Task RefreshEarthTrackAsync() { earthLastTrack = default; return UpdateEarthAsync(); }

    private async Task UpdateEarthAsync()
    {
        if (earthBusy) return;
        earthBusy = true;
        try
        {
            var request = ReadTrackingRequest();
            var now = DateTimeOffset.UtcNow;
            request.UtcUnixMilliseconds = 0;
            int minutes = (int)earthMinutes.Value;
            bool full = earthLastTrack == default || now - earthLastTrack >= TimeSpan.FromSeconds(30) || !earthLive.Checked || !request.Equals(earthLastRequest);
            long start = (full ? now.AddMinutes(-minutes) : now).ToUnixTimeMilliseconds();
            long end = (full ? now.AddMinutes(minutes) : now).ToUnixTimeMilliseconds();
            var result = await client.GroundTrackAsync(new GroundTrackRequest
            {
                Tracking = request, StartUtcMilliseconds = start, EndUtcMilliseconds = end,
                StepSeconds = Math.Max(1, (int)Math.Ceiling(minutes * 120.0 / 360))
            }, lifetime.Token);
            if (IsDisposed) return;
            earthUtc = now.ToUnixTimeMilliseconds();
            if (full) earthFull = result;
            earthCurrent = result;
            earthMap.SetTrack(result, earthUtc, full);
            earthGpu?.SetTrack(result, earthUtc, full);
            if (full) { earthLastTrack = now; earthLastRequest = request.Clone(); }
            earthStatus.Text = $"{result.CelestialTarget} NORAD {result.NoradNumber} · UTC {now:HH:mm:ss} · {string.Join("/", result.Points.Select(p => p.OrbitBackend).Distinct())}";
        }
        finally { earthBusy = false; }
    }
}
