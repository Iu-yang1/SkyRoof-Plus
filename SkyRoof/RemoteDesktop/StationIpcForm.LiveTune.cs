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

    private Control CreateLiveTuningRow()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(Row("LIVE TUNING (Hz; blank = unchanged)", "Position", livePosition, "RIT", liveRit, "XIT", liveXit));
        panel.Controls.Add(Row("RX manual", liveRxManual, "TX manual", liveTxManual,
            ActionButton("UPDATE ACTIVE LINK", TuneActiveLinkAsync)));
        return panel;
    }

    private static double? OptionalHz(TextBox value) =>
        string.IsNullOrWhiteSpace(value.Text) ? null : double.Parse(value.Text, CultureInfo.InvariantCulture);

    private async Task TuneActiveLinkAsync()
    {
        var job = await client.TrackingAsync(lifetime.Token);
        if (!job.Running) throw new InvalidOperationException("Start a satellite tracking job first.");
        var patch = new TrackingJobTune { JobId = job.JobId };
        if (OptionalHz(livePosition) is { } position) patch.TransponderOffsetHz = position;
        if (OptionalHz(liveRit) is { } rit) patch.RitHz = rit;
        if (OptionalHz(liveXit) is { } xit) patch.XitHz = xit;
        if (OptionalHz(liveRxManual) is { } rx) patch.DownlinkManualHz = rx;
        if (OptionalHz(liveTxManual) is { } tx) patch.UplinkManualHz = tx;
        var result = await client.TuneTrackingAsync(patch, lifetime.Token);
        trackingResult.Text = $"{result.JobId} · {result.Phase} · {result.Error}";
    }
}
