using System.Globalization;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly TextBox livePosition = new() { Width = 80 };
    private readonly TextBox liveRit = new() { Width = 80 };
    private readonly TextBox liveXit = new() { Width = 80 };
    private readonly TextBox liveRxManual = new() { Width = 80 };
    private readonly TextBox liveTxManual = new() { Width = 80 };
    private readonly TextBox liveRxBase = new() { Width = 80 };
    private readonly TextBox liveTxBase = new() { Width = 80 };
    private readonly Label liveOffsets = new() { AutoSize = true, MaximumSize = new Size(750, 0) };
    private TrackingJobStatus? activeTracking;

    private Control CreateLiveTuningRow()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(Row("LIVE TUNING (Hz; blank = unchanged)", "Position", livePosition, "RIT", liveRit, "XIT", liveXit));
        panel.Controls.Add(Row("RX manual", liveRxManual, "TX manual", liveTxManual, "RX base Δ", liveRxBase, "TX base Δ", liveTxBase));
        panel.Controls.Add(Row(ActionButton("UPDATE ACTIVE LINK", TuneActiveLinkAsync), ActionButton("LOAD ACTIVE OFFSETS", LoadActiveOffsetsAsync)));
        panel.Controls.Add(liveOffsets);
        return panel;
    }

    private void ShowTrackingJob(TrackingJobStatus job)
    {
        if (IsDisposed) return;
        activeTracking = job;
        tracking.Text = $"TRACK · {job.Phase} · {job.CompletedTicks} ticks · ROT {(job.PointRotator ? "TRACK" : "OFF")}";
        liveOffsets.Text = job.ActiveRadioLink is { } link
            ? $"ACTIVE · POS {link.TransponderOffsetHz:R} · RIT {link.RitHz:R} · XIT {link.XitHz:R} · RX/TX manual {link.DownlinkManualHz:R}/{link.UplinkManualHz:R} · base {link.DownlinkBaseOffsetHz}/{link.UplinkBaseOffsetHz} Hz"
            : "No active satellite RadioLink";
        if (job.Running && job.Latest is { } latest)
            trackingResult.Text = $"{job.Phase} · AZ {latest.AzimuthDegrees:F2} EL {latest.ElevationDegrees:F2} · RX {latest.RxHz} TX {latest.TxHz} · actual {latest.ActualRxHz}/{latest.ActualTxHz} · {job.Error}";

    }

    private Task LoadActiveOffsetsAsync()
    {
        var link = activeTracking?.ActiveRadioLink ?? throw new InvalidOperationException("No active satellite RadioLink.");
        livePosition.Text = Number(link.TransponderOffsetHz);
        liveRit.Text = Number(link.RitHz);
        liveXit.Text = Number(link.XitHz);
        liveRxManual.Text = Number(link.DownlinkManualHz);
        liveTxManual.Text = Number(link.UplinkManualHz);
        liveRxBase.Text = link.DownlinkBaseOffsetHz.ToString(CultureInfo.InvariantCulture);
        liveTxBase.Text = link.UplinkBaseOffsetHz.ToString(CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    }

    private static double? OptionalHz(TextBox value) =>
        string.IsNullOrWhiteSpace(value.Text) ? null : double.Parse(value.Text, CultureInfo.InvariantCulture);

    private async Task TuneActiveLinkAsync()
    {
        var job = activeTracking;
        if (job is not { Running: true, ActiveRadioLink: not null }) throw new InvalidOperationException("Start a satellite tracking job first.");
        var patch = new TrackingJobTune { JobId = job.JobId };
        if (OptionalHz(livePosition) is { } position) patch.TransponderOffsetHz = position;
        if (OptionalHz(liveRit) is { } rit) patch.RitHz = rit;
        if (OptionalHz(liveXit) is { } xit) patch.XitHz = xit;
        if (OptionalHz(liveRxManual) is { } rx) patch.DownlinkManualHz = rx;
        if (OptionalHz(liveTxManual) is { } tx) patch.UplinkManualHz = tx;
        if (!string.IsNullOrWhiteSpace(liveRxBase.Text)) patch.DownlinkBaseOffsetHz = ReadHz(liveRxBase);
        if (!string.IsNullOrWhiteSpace(liveTxBase.Text)) patch.UplinkBaseOffsetHz = ReadHz(liveTxBase);
        var result = await client.TuneTrackingAsync(patch, lifetime.Token);
        ShowTrackingJob(result);
        foreach (var box in new[] { livePosition, liveRit, liveXit, liveRxManual, liveTxManual, liveRxBase, liveTxBase }) box.Clear();
    }
}
