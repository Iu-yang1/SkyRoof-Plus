using System.Globalization;
using Google.Protobuf;
using SkyRoof.Remote.Foundation.Digital;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly ComboBox digitalMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox q65Submode = new() { Text = "Q65-60B", Width = 100 };
    private readonly TextBox digitalMessage = new() { Width = 260 };
    private readonly TextBox digitalAudioHz = new() { Text = "1500", Width = 75 };
    private readonly TextBox digitalGain = new() { Text = "0.5", Width = 60 };
    private readonly CheckBox digitalOdd = new() { Text = "Odd period", AutoSize = true };
    private readonly TextBox q65Config = new() { Multiline = true, Width = 560, Height = 100, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox telemetryConfig = new() { Multiline = true, Width = 560, Height = 140, ScrollBars = ScrollBars.Vertical };
    private readonly Label digitalStatus = new() { AutoSize = true };
    private readonly TextBox cwHistory = new() { Multiline = true, Width = 560, Height = 100, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private CancellationTokenSource? digitalCancellation;

    private TabPage CreateDigitalPage()
    {
        var page = new TabPage("DIGITAL / TELEMETRY");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        digitalMode.Items.AddRange(new object[] { "FT4", "Q65" });
        digitalMode.SelectedIndex = 0;
        layout.Controls.Add(Row(digitalMode, "Q65 submode", q65Submode, "Audio Hz", digitalAudioHz, "Gain", digitalGain, digitalOdd));
        layout.Controls.Add(Row("Message", digitalMessage, ActionButton("TIMED TX", SendDigitalAsync),
            ActionButton("CANCEL TX", () => StopStream(digitalCancellation)), ActionButton("RESUME DIGITAL", ResumeDigitalAsync)));
        layout.Controls.Add(digitalStatus);
        layout.Controls.Add(Row("CW decoder", ActionButton("START NEW CW", () => ConfigureCwRxAsync(true)),
            ActionButton("STOP CW RX", () => ConfigureCwRxAsync(false)), ActionButton("CW HISTORY", LoadCwHistoryAsync)));
        layout.Controls.Add(cwHistory);
        q65Config.Text = JsonFormatter.Default.Format(new Q65RxConfiguration { Enabled = false, Submode = q65Submode.Text, RxHz = 1500, ToleranceHz = 100 });
        layout.Controls.Add(Row("Q65 RX configuration (protobuf JSON)", ActionButton("APPLY Q65", ApplyQ65Async)));
        layout.Controls.Add(q65Config);
        telemetryConfig.Text = JsonFormatter.Default.Format(new TelemetryRxConfiguration { Enabled = false, SaveFrames = true });
        layout.Controls.Add(Row("Telemetry configuration (protobuf JSON)", ActionButton("APPLY TELEMETRY", ApplyTelemetryAsync)));
        layout.Controls.Add(telemetryConfig);
        page.Controls.Add(layout);
        return page;
    }

    private async Task SendDigitalAsync()
    {
        if (digitalCancellation is not null || pttClosing) return;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        digitalCancellation = source;
        try
        {
            digitalStatus.Text = "Encoding and sealing waveform…";
            var (waveform, target) = await PrepareDigitalAsync(source.Token);
            var hz = long.Parse(tx.Text, CultureInfo.InvariantCulture);
            digitalStatus.Text = $"ARMED · {DateTime.UnixEpoch.AddTicks(target / 100):HH:mm:ss.fff} UTC";
            var result = await client.TimedTxAsync(waveform, hz, target, source.Token);
            if (!IsDisposed) digitalStatus.Text = $"{result.State} · {result.SourceSamples} samples";
        }
        finally { digitalCancellation = null; }
    }

    private async Task<(EncodedWaveform Waveform, long Target)> PrepareDigitalAsync(CancellationToken token)
    {
        float audioHz = float.Parse(digitalAudioHz.Text, CultureInfo.InvariantCulture);
        float gain = float.Parse(digitalGain.Text, CultureInfo.InvariantCulture);
        if (digitalMode.SelectedItem?.ToString() == "FT4")
        {
            var encoded = await client.EncodeFt4Async(new Ft4EncodeRequest { Message = digitalMessage.Text, AudioHz = audioHz, Gain = gain }, token);
            const long slot = 7_500_000_000;
            long now = (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;
            long index = (now + 3_000_000_000 + slot - 1) / slot;
            if ((index % 2 == 1) != digitalOdd.Checked) index++;
            return (encoded, checked(index * slot + 150_000_000 - TxDelay("FT4") * 1_000_000));
        }
        var q65 = await client.EncodeQ65Async(new Q65EncodeRequest { Submode = q65Submode.Text, Message = digitalMessage.Text, AudioHz = audioHz, Gain = gain }, token);
        long nowNs = (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;
        long delay = TxDelay("Q65");
        long chosen = Q65SlotTiming.Select(nowNs, q65.SlotSeconds, q65.TxStartSamples12K, delay, digitalOdd.Checked);
        return (q65.Waveform, Q65SlotTiming.Target(chosen, q65.SlotSeconds, q65.TxStartSamples12K, delay));
    }

    private static long TxDelay(string mode) =>
        long.TryParse(Environment.GetEnvironmentVariable("SKYROOF_" + mode + "_TX_DELAY_MS"), out var delay) ? delay : 0;

    private async Task ResumeDigitalAsync() =>
        ShowRadio(await client.SetRadioAsync(DeviceOperation.DeviceResumeDigitalTx, 0, "", lifetime.Token));

    private async Task ConfigureCwRxAsync(bool enabled)
    {
        var result = await client.ConfigureCwAsync(new CwRxConfiguration { Enabled = enabled, Mode = "NewCW" }, lifetime.Token);
        digitalStatus.Text = $"CW RX · {result.Enabled} · {result.Mode}";
    }

    private async Task LoadCwHistoryAsync()
    {
        var history = await client.ReadCwHistoryAsync(lifetime.Token);
        if (!IsDisposed) cwHistory.Text = history.Error + Environment.NewLine +
            string.Join(Environment.NewLine, history.Entries.Select(x => $"{x.LastWindowEndUtc} · {x.CommittedText}"));
    }

    private async Task ApplyQ65Async()
    {
        var request = JsonParser.Default.Parse<Q65RxConfiguration>(q65Config.Text);
        var result = await client.ConfigureQ65Async(request, lifetime.Token);
        q65Config.Text = JsonFormatter.Default.Format(result);
    }

    private async Task ApplyTelemetryAsync()
    {
        var request = JsonParser.Default.Parse<TelemetryRxConfiguration>(telemetryConfig.Text);
        var result = await client.ConfigureTelemetryAsync(request, lifetime.Token);
        telemetryConfig.Text = JsonFormatter.Default.Format(result);
    }
}
