using NAudio.CoreAudioApi;
using SkyRoof.AFC;
using SkyRoof.Remote.Ipc;
using VE3NEA;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly ComboBox afcDevice = new() { Width = 310, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox afcLoopback = new() { Text = "IF playback loopback", AutoSize = true };
    private readonly CheckBox afcRight = new() { Text = "Right IF channel", AutoSize = true };
    private readonly ComboBox afcMode = new() { Width = 75, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox afcControl = new() { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox afcPreset = new() { Width = 105, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox afcSign = new() { Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown afcTarget = new() { Minimum = -6000, Maximum = 6000, Width = 80 };
    private readonly NumericUpDown afcCenter = new() { Minimum = -6000, Maximum = 6000, Width = 80 };
    private readonly NumericUpDown afcMax = new() { Minimum = 1, Maximum = 1000000, Value = 2000, Width = 80 };
    private readonly CheckBox afcPilot = new() { Text = "SSB pilot", AutoSize = true };
    private readonly CheckBox afcCenterEnabled = new() { Text = "Center", AutoSize = true };
    private readonly CheckBox afcRelease = new() { Text = "Gentle release", AutoSize = true };
    private readonly Label afcStatus = new() { Dock = DockStyle.Bottom, Height = 45 };
    private readonly AfcSpectrumView afcSpectrum = new() { Dock = DockStyle.Fill };
    private CancellationTokenSource? afcSource;
    private AfcIfInput? afcInput;
    private bool afcRetargetPending;
    private sealed record IfDevice(string Id, string Name) { public override string ToString() => Name; }

    private TabPage CreateAfcPage()
    {
        var page = new TabPage("AFC / 12 kHz IF");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        afcMode.Items.AddRange(Enum.GetNames<AfcSignalMode>()); afcMode.SelectedIndex = 0;
        afcControl.Items.AddRange(new object[] { "Assist", "Auto" }); afcControl.SelectedIndex = 0;
        afcPreset.Items.AddRange(Enum.GetNames<AfcAcquisitionPreset>()); afcPreset.SelectedIndex = 0;
        afcSign.Items.AddRange(new object[] { "0", "1", "-1" }); afcSign.SelectedIndex = 0;
        panel.Controls.Add(Row("Explicit 12 kHz IF endpoint", afcDevice, afcLoopback, ActionButton("SCAN IF", ScanIfAsync), afcRight));
        panel.Controls.Add(Row(afcMode, afcControl, afcPreset, "Sign", afcSign, afcPilot, "Max Hz", afcMax, afcRelease));
        panel.Controls.Add(Row("Search Hz", afcTarget, afcCenterEnabled, afcCenter, ActionButton("START AFC", StartAfcAsync),
            ActionButton("APPLY TARGET", QueueAfcTargetAsync), ActionButton("STOP AFC", () => StopStream(afcSource))));
        afcLoopback.CheckedChanged += (_, _) => { afcDevice.Items.Clear(); };
        afcSpectrum.TargetSelected += hz =>
        {
            afcTarget.Value = (decimal)Math.Clamp(hz, -6000, 6000);
            if (afcSource is not null) afcRetargetPending = true;
        };
        panel.Controls.Add(Row(ActionButton("START STATION AFC", StartStationAfcAsync),
            ActionButton("STATION AFC STATUS", ReadStationAfcAsync), ActionButton("APPLY STATION TARGET", RetargetStationAfcAsync),
            ActionButton("STOP STATION AFC", StopStationAfcAsync)));
        panel.Controls.Add(Row("Station tracking owns CAT. Station AFC uses its configured actual 12 kHz IF endpoint."));
        page.Controls.Add(afcSpectrum); page.Controls.Add(panel); page.Controls.Add(afcStatus);
        return page;
    }

    private AfcCaptureTarget StationAfcTarget()
    {
        var target = new AfcCaptureTarget { TargetHz = (double)afcTarget.Value, SsbPilot = afcPilot.Checked, Preset = afcPreset.Text };
        if (afcCenterEnabled.Checked) target.CenterHz = (double)afcCenter.Value;
        return target;
    }

    private async Task StartStationAfcAsync()
    {
        if (activeTracking is not { Running: true } job) throw new InvalidOperationException("Start Station tracking first.");
        var profile = new AfcOpenRequest { JobId = job.JobId, Mode = afcMode.Text, Control = afcControl.Text,
            Sign = int.Parse(afcSign.Text), MaxCorrectionHz = (double)afcMax.Value,
            FmDeadbandHz = 20, ToneDeadbandHz = 5, GentleRelease = afcRelease.Checked };
        if (afcCenterEnabled.Checked) profile.CenterHz = (double)afcCenter.Value;
        ShowStationAfc(await client.StartStationAfcAsync(new AfcCaptureStart { Control = profile, Target = StationAfcTarget() }, lifetime.Token));
    }
    private async Task ReadStationAfcAsync() => ShowStationAfc(await client.StationAfcAsync(lifetime.Token));
    private async Task RetargetStationAfcAsync() => ShowStationAfc(await client.RetargetStationAfcAsync(StationAfcTarget(), lifetime.Token));
    private async Task StopStationAfcAsync() => ShowStationAfc(await client.StopStationAfcAsync(lifetime.Token));
    private void ShowStationAfc(AfcCaptureStatus value)
    {
        if (IsDisposed) return;
        afcStatus.Text = value.Afc is { } afc
            ? $"Station IF · {value.Detail} · {value.Samples} samples · {afc.State} · IF {afc.ObservedHz:F1} Hz · correction {afc.CorrectionHz:F1} Hz"
            : "Station IF · " + value.Detail;
    }

    private Task QueueAfcTargetAsync()
    {
        if (afcSource is not null) afcRetargetPending = true;
        return Task.CompletedTask;
    }

    private Task ScanIfAsync()
    {
        using var devices = new MMDeviceEnumerator();
        var endpoints = devices.EnumerateAudioEndPoints(afcLoopback.Checked ? DataFlow.Render : DataFlow.Capture, DeviceState.Active);
        afcDevice.Items.Clear();
        foreach (var device in endpoints) { using (device) afcDevice.Items.Add(new IfDevice(device.ID, device.FriendlyName)); }
        if (afcDevice.Items.Count > 0) afcDevice.SelectedIndex = 0;
        return Task.CompletedTask;
    }

    private async Task StartAfcAsync()
    {
        if (afcSource is not null) return;
        if (activeTracking is not { Running: true } job) throw new InvalidOperationException("Start Station tracking first.");
        if (afcDevice.SelectedItem is not IfDevice selected) throw new InvalidOperationException("Select the actual 12 kHz IF endpoint.");
        var modeValue = Enum.Parse<AfcSignalMode>(afcMode.Text);
        var config = new AfcOpenRequest { JobId = job.JobId, Mode = afcMode.Text, Control = afcControl.Text,
            Sign = int.Parse(afcSign.Text), MaxCorrectionHz = (double)afcMax.Value,
            FmDeadbandHz = 20, ToneDeadbandHz = 5, GentleRelease = afcRelease.Checked };
        if (afcCenterEnabled.Checked) config.CenterHz = (double)afcCenter.Value;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        afcSource = source;
        afcRetargetPending = false;
        string id = "";
        try
        {
            var opened = await client.OpenAfcAsync(config, source.Token);
            id = opened.SessionId;
            using var input = new AfcIfInput();
            afcInput = input;
            input.SetAcquisitionPreset(Enum.Parse<AfcAcquisitionPreset>(afcPreset.Text));
            input.DisplayEnabled = true;
            input.Start(afcLoopback.Checked ? null : selected.Id, 12000, modeValue, afcPilot.Checked, (double)afcTarget.Value,
                afcLoopback.Checked ? AfcIfSource.PlaybackLoopback : AfcIfSource.WasapiCapture,
                afcLoopback.Checked ? selected.Id : null, afcRight.Checked ? AudioChannelChoice.Right : AudioChannelChoice.Left);
            await RunAfcLoopAsync(input, id, modeValue, source.Token);
        }
        finally
        {
            afcInput = null; afcSource = null; afcRetargetPending = false;
            if (id.Length > 0) await RunAsync(async () => { await client.CloseAfcAsync(id, CancellationToken.None); });
        }
    }

    private async Task RunAfcLoopAsync(AfcIfInput input, string id, AfcSignalMode modeValue, CancellationToken token)
    {
        AfcIfDisplayFrame? display = null;
        var gate = new object();
        void Receive(AfcIfDisplayFrame frame) { lock (gate) display = frame; }
        input.DisplayFrameAvailable += Receive;
        DateTime sent = default;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var update = new AfcUpdateRequest { SessionId = id };
                if (afcRetargetPending)
                {
                    afcRetargetPending = false;
                    update.Target = RebaseAfcInput(input, modeValue);
                    sent = default;
                }
                else if (input.Diagnostics.LastObservation is { } obs && obs.SampleUtc > sent)
                {
                    sent = obs.SampleUtc;
                    update.Measurement = new AfcMeasurement { SampleUtcMilliseconds = new DateTimeOffset(obs.SampleUtc).ToUnixTimeMilliseconds(),
                        Generation = obs.Generation, IfOffsetHz = obs.IfOffsetHz, SigmaHz = obs.SigmaHz, Confidence = obs.Confidence,
                        AbsoluteReference = obs.HasAbsoluteReference, Ambiguous = obs.Ambiguous };
                }
                var status = await client.UpdateAfcAsync(update, token);
                if (IsDisposed) return;
                AfcIfDisplayFrame? frame;
                lock (gate) { frame = display; display = null; }
                if (frame is { } current) afcSpectrum.SetDisplayFrame(current, status.Confidence > 0 ? status.ObservedHz : null, (double)afcTarget.Value);
                afcStatus.Text = $"{status.State} · IF {status.ObservedHz:F1} Hz · residual {status.ErrorHz:F1} · RX correction {status.CorrectionHz:F1} Hz · {status.Detail}";
                await Task.Delay(200, token);
            }
        }
        finally { input.DisplayFrameAvailable -= Receive; }
    }
    private AfcTargetChange RebaseAfcInput(AfcIfInput input, AfcSignalMode modeValue)
    {
        input.SetAcquisitionPreset(Enum.Parse<AfcAcquisitionPreset>(afcPreset.Text));
        input.UpdateTarget(modeValue, afcPilot.Checked, (double)afcTarget.Value);
        input.InvalidateTarget();
        var target = new AfcTargetChange { Generation = input.Generation };
        if (afcCenterEnabled.Checked) target.CenterHz = (double)afcCenter.Value;
        return target;
    }
}
