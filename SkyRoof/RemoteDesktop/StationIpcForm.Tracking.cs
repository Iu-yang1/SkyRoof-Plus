using Google.Protobuf;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly TextBox catalogQuery = new() { Width = 200 };
    private readonly ComboBox satellites = new() { Width = 270, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox transmitters = new() { Width = 270, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox trackingConfig = new() { Width = 650, Height = 240, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox parkConfig = new() { Width = 650, Height = 70, Multiline = true };
    private readonly CheckBox resumeTrack = new() { Text = "Resume after restart", AutoSize = true };
    private readonly Label trackingResult = new() { AutoSize = true, MaximumSize = new Size(750, 0) };
    private IReadOnlyList<CatalogSatellite> catalogItems = [];

    private TabPage CreateTrackingPage()
    {
        var page = new TabPage("CATALOG / TRACKING / EME");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        layout.Controls.Add(Row("Search", catalogQuery, ActionButton("SEARCH", SearchCatalogAsync)));
        layout.Controls.Add(Row("Satellite", satellites, "Transmitter", transmitters, ActionButton("USE", UseCatalogSelectionAsync)));
        satellites.SelectedIndexChanged += (_, _) => PopulateTransmitters();
        layout.Controls.Add(CreateTrackingFields());
        layout.Controls.Add(Row("Advanced request · TLE/OMM, RadioLink, RIT/XIT (protobuf JSON)"));
        trackingConfig.Text = JsonFormatter.Default.Format(new TrackingRequest { CorrectDownlink = true, CorrectUplink = true, PointRotator = false });
        layout.Controls.Add(trackingConfig);
        layout.Controls.Add(Row(ActionButton("PREDICT", PredictAsync), ActionButton("START TRACK", StartTrackingAsync),
            ActionButton("STOP TRACK", StopTrackingAsync), resumeTrack));
        layout.Controls.Add(trackingResult);
        parkConfig.Text = JsonFormatter.Default.Format(new RotatorParkRequest { ToleranceDegrees = 1, SegmentTimeoutSeconds = 60 });
        layout.Controls.Add(Row("Park waypoints (protobuf JSON)", ActionButton("PARK", ParkAsync)));
        layout.Controls.Add(parkConfig);
        page.Controls.Add(layout);
        return page;
    }

    private async Task SearchCatalogAsync()
    {
        var result = await client.SearchCatalogAsync(catalogQuery.Text, lifetime.Token);
        catalogItems = result.Satellites.ToArray();
        satellites.Items.Clear();
        foreach (var satellite in catalogItems) satellites.Items.Add($"{satellite.Name} · {satellite.Norad}");
        if (satellites.Items.Count > 0) satellites.SelectedIndex = 0;
    }

    private void PopulateTransmitters()
    {
        transmitters.Items.Clear();
        int index = satellites.SelectedIndex;
        if (index < 0 || index >= catalogItems.Count) return;
        foreach (var transmitter in catalogItems[index].Transmitters) transmitters.Items.Add(transmitter.Description);
        if (transmitters.Items.Count > 0) transmitters.SelectedIndex = 0;
    }

    private Task UseCatalogSelectionAsync()
    {
        if (satellites.SelectedIndex < 0 || transmitters.SelectedIndex < 0) return Task.CompletedTask;
        var satellite = catalogItems[satellites.SelectedIndex];
        var transmitter = satellite.Transmitters[transmitters.SelectedIndex];
        var request = JsonParser.Default.Parse<TrackingRequest>(trackingConfig.Text);
        request.CatalogSatelliteId = satellite.SatelliteId;
        request.TleLine1 = satellite.TleLine1;
        request.TleLine2 = satellite.TleLine2;
        request.CelestialTarget = "";
        request.RadioLink = transmitter.Link?.Clone() ?? new RadioLinkProfile();
        request.DownlinkHz = request.RadioLink.DownlinkLowHz;
        request.UplinkHz = request.RadioLink.UplinkLowHz;
        trackingConfig.Text = JsonFormatter.Default.Format(request);
        linkRx.Text = request.DownlinkHz.ToString(System.Globalization.CultureInfo.InvariantCulture);
        linkTx.Text = request.UplinkHz.ToString(System.Globalization.CultureInfo.InvariantCulture);
        moonTarget.Checked = false;
        return Task.CompletedTask;
    }

    private TrackingRequest ReadTrackingRequest()
    {
        var request = JsonParser.Default.Parse<TrackingRequest>(trackingConfig.Text);
        request.UtcUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return request;
    }

    private async Task PredictAsync()
    {
        var result = await client.PredictAsync(ReadTrackingRequest(), lifetime.Token);
        trackingResult.Text = $"AZ {result.AzimuthDegrees:F2} EL {result.ElevationDegrees:F2} · RX {result.RxHz} · TX {result.TxHz} · {result.OrbitBackend} {result.DataQuality}";
        if (result.EmePath is { } path) trackingResult.Text += $" · DX EL {path.PeerElevationDegrees:F2}° · delays {path.IncomingDelaySeconds:F6}/{path.OutgoingDelaySeconds:F6} s";
    }

    private async Task StartTrackingAsync()
    {
        var result = await client.StartTrackingAsync(ReadTrackingRequest(), resumeTrack.Checked, lifetime.Token);
        trackingResult.Text = $"{result.JobId} · {result.Phase} · {result.Error}";
    }

    private async Task StopTrackingAsync()
    {
        var current = await client.TrackingAsync(lifetime.Token);
        if (string.IsNullOrEmpty(current.JobId)) return;
        var result = await client.StopTrackingAsync(current.JobId, lifetime.Token);
        trackingResult.Text = $"{result.Phase} · {result.Error}";
    }

    private async Task ParkAsync() =>
        ShowRotator(await client.ParkAsync(JsonParser.Default.Parse<RotatorParkRequest>(parkConfig.Text), lifetime.Token));
}
