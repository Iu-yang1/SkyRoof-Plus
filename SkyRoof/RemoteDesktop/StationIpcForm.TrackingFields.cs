using System.Globalization;
using Google.Protobuf;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly TextBox siteLat = new() { Width = 75 };
    private readonly TextBox siteLon = new() { Width = 75 };
    private readonly TextBox siteHeight = new() { Width = 60, Text = "0" };
    private readonly TextBox siteMask = new() { Width = 60, Text = "0" };
    private readonly TextBox linkRx = new() { Width = 100 };
    private readonly TextBox linkTx = new() { Width = 100, Text = "0" };
    private readonly CheckBox moonTarget = new() { Text = "MOON / EME", AutoSize = true };
    private readonly ComboBox emeDoppler = new() { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox peerLat = new() { Width = 75 };
    private readonly TextBox peerLon = new() { Width = 75 };
    private readonly TextBox peerHeight = new() { Width = 60, Text = "0" };
    private readonly CheckBox rxDoppler = new() { Text = "RX Doppler", Checked = true, AutoSize = true };
    private readonly CheckBox txDoppler = new() { Text = "TX Doppler", Checked = true, AutoSize = true };
    private readonly CheckBox pointTracking = new() { Text = "Point rotator", AutoSize = true };

    private Control CreateTrackingFields()
    {
        emeDoppler.Items.AddRange(new object[] { "cfom", "own-echo-rx", "own-echo-tx", "dx-fixed", "none" });
        emeDoppler.SelectedIndex = 0;
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(Row("LAT °", siteLat, "LON °", siteLon, "Height m", siteHeight, "Mask °", siteMask));
        panel.Controls.Add(Row("Downlink Hz", linkRx, "Uplink Hz", linkTx, moonTarget, emeDoppler));
        panel.Controls.Add(Row("DX LAT °", peerLat, "DX LON °", peerLon, "DX height m", peerHeight));
        panel.Controls.Add(Row(rxDoppler, txDoppler, pointTracking,
            ActionButton("APPLY FIELDS", ApplyTrackingFieldsAsync), ActionButton("LOAD JSON FIELDS", ReadTrackingFieldsAsync)));
        return panel;
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static double ReadNumber(TextBox box) => double.Parse(box.Text, CultureInfo.InvariantCulture);
    private static long ReadHz(TextBox box) => long.Parse(box.Text, CultureInfo.InvariantCulture);

    private void LoadTrackingFields(TrackingRequest request)
    {
        siteLat.Text = Number(request.LatitudeDegrees);
        siteLon.Text = Number(request.LongitudeDegrees);
        siteHeight.Text = Number(request.HeightMetres);
        siteMask.Text = Number(request.ElevationMaskDegrees);
        linkRx.Text = request.DownlinkHz.ToString(CultureInfo.InvariantCulture);
        linkTx.Text = request.UplinkHz.ToString(CultureInfo.InvariantCulture);
        moonTarget.Checked = request.CelestialTarget == "MOON";
        emeDoppler.SelectedItem = string.IsNullOrEmpty(request.EmeDopplerMode) ? "cfom" : request.EmeDopplerMode;
        rxDoppler.Checked = request.CorrectDownlink;
        txDoppler.Checked = request.CorrectUplink;
        pointTracking.Checked = request.PointRotator;
        peerLat.Text = request.EmePeer is { } peer ? Number(peer.LatitudeDegrees) : "";
        peerLon.Text = request.EmePeer is { } dx ? Number(dx.LongitudeDegrees) : "";
        peerHeight.Text = Number(request.EmePeer?.HeightMetres ?? 0);
    }

    private Task ReadTrackingFieldsAsync()
    {
        LoadTrackingFields(JsonParser.Default.Parse<TrackingRequest>(trackingConfig.Text));
        return Task.CompletedTask;
    }

    private Task ApplyTrackingFieldsAsync()
    {
        var request = JsonParser.Default.Parse<TrackingRequest>(trackingConfig.Text);
        request.LatitudeDegrees = ReadNumber(siteLat);
        request.LongitudeDegrees = ReadNumber(siteLon);
        request.HeightMetres = ReadNumber(siteHeight);
        request.ElevationMaskDegrees = ReadNumber(siteMask);
        request.DownlinkHz = ReadHz(linkRx);
        request.UplinkHz = ReadHz(linkTx);
        request.CorrectDownlink = rxDoppler.Checked;
        request.CorrectUplink = txDoppler.Checked;
        request.PointRotator = pointTracking.Checked;
        ApplyEmeFields(request);
        trackingConfig.Text = JsonFormatter.Default.Format(request);
        trackingResult.Text = "Fields applied to request.";
        return Task.CompletedTask;
    }

    private void ApplyEmeFields(TrackingRequest request)
    {
        request.CelestialTarget = moonTarget.Checked ? "MOON" : "";
        request.EmeDopplerMode = moonTarget.Checked ? emeDoppler.Text : "";
        request.EmePeer = moonTarget.Checked && emeDoppler.Text == "dx-fixed" ?
            new EmePeerSite { LatitudeDegrees = ReadNumber(peerLat), LongitudeDegrees = ReadNumber(peerLon), HeightMetres = ReadNumber(peerHeight) } : null;
        if (moonTarget.Checked)
        {
            request.RadioLink = null;
            request.CatalogSatelliteId = "";
        }
    }
}
