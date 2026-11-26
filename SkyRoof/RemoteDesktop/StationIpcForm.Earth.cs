using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly RemoteEarthMap earthMap = new() { Dock = DockStyle.Fill };
    private readonly Label earthStatus = new() { Dock = DockStyle.Bottom, Height = 30 };
    private readonly CheckBox earthLive = new() { Text = "Live (selected target)", AutoSize = true };
    private readonly NumericUpDown earthMinutes = new() { Minimum = 1, Maximum = 1440, Value = 100, Width = 65 };
    private bool earthBusy;
    private DateTimeOffset earthLastTrack;
    private TrackingRequest? earthLastRequest;

    private TabPage CreateEarthPage()
    {
        var page = new TabPage("EARTH VIEW");
        var row = Row("Past / future minutes", earthMinutes, ActionButton("GROUND TRACK", RefreshEarthTrackAsync), earthLive);
        row.Dock = DockStyle.Top;
        page.Controls.Add(earthMap);
        page.Controls.Add(row);
        page.Controls.Add(earthStatus);
        return page;
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
            earthMap.SetTrack(result, now.ToUnixTimeMilliseconds(), full);
            if (full) { earthLastTrack = now; earthLastRequest = request.Clone(); }
            earthStatus.Text = $"{result.CelestialTarget} NORAD {result.NoradNumber} · UTC {now:HH:mm:ss} · {string.Join("/", result.Points.Select(p => p.OrbitBackend).Distinct())}";
        }
        finally { earthBusy = false; }
    }
}
