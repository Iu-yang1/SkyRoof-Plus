using Google.Protobuf;
using NAudio.Wave;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly NumericUpDown offlineOffset = new() { Minimum = 0, Maximum = 1000000, DecimalPlaces = 3, Increment = 0.1m, Width = 100 };
    private readonly ComboBox offlineMode = new() { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox offlineRight = new() { Text = "Right AF", AutoSize = true };
    private readonly TextBox offlineResults = new() { Multiline = true, Width = 750, Height = 100, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

    private void AddOfflineAfControls(FlowLayoutPanel layout)
    {
        offlineMode.Items.AddRange(new object[] { "FT4", "Q65" });
        offlineMode.SelectedIndex = 0;
        layout.Controls.Add(Row("AF slot start (seconds from WAV start)", offlineOffset, offlineMode, offlineRight,
            ActionButton("DECODE AF SLOT", DecodeAfSlotAsync)));
        layout.Controls.Add(offlineResults);
    }

    private async Task DecodeAfSlotAsync()
    {
        if (replaySource is not null) return;
        string path = SelectedRecording();
        string mode = offlineMode.Text;
        double offset = (double)offlineOffset.Value;
        int selectedChannel = offlineRight.Checked ? 1 : 0;
        var profile = mode == "Q65" ? JsonParser.Default.Parse<Q65RxConfiguration>(q65Config.Text) : null;
        int samples = 290304;
        if (profile is not null)
        {
            profile.Submode = q65Submode.Text.Trim().ToUpperInvariant();
            if (profile.Submode.Length < 2 || !int.TryParse(profile.Submode[..^1], out int seconds) ||
                seconds is not (15 or 30 or 60 or 120 or 300))
                throw new InvalidDataException("Choose a valid Q65 submode.");
            samples = checked(seconds * 48000);
        }
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        replaySource = source;
        try
        {
            offlineResults.Text = "Reading selected AF slot; no live RX injection...";
            var pcm = await Task.Run(() => ReadAfWindow(path, offset, selectedChannel, samples), source.Token);
            var request = new OfflineAfDecodeRequest { Mode = mode, SampleRate = 48000, Pcm16 = ByteString.CopyFrom(pcm) };
            if (profile is not null) request.Q65 = profile;
            var result = await client.DecodeOfflineAfAsync(request, source.Token);
            if (!IsDisposed)
                offlineResults.Text = string.Join(Environment.NewLine, result.Messages.Concat(
                    result.Rows.Select(row => $"{row.SnrDb:F1} dB · {row.FrequencyHz:F1} Hz · DT {row.DtSeconds:F2} · {row.Text}")));
            if (!IsDisposed && offlineResults.TextLength == 0) offlineResults.Text = "No decoded messages in selected window.";
        }
        finally { replaySource = null; }
    }

    internal static byte[] ReadAfWindow(string path, double offset, int channel, int samples)
    {
        using var reader = new WaveFileReader(path);
        var format = reader.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 ||
            format.SampleRate != 48000 || channel < 0 || channel >= format.Channels)
            throw new InvalidDataException("Choose a 48 kHz PCM16 AF WAV with the selected channel.");
        long position = checked((long)Math.Round(offset * 48000) * format.BlockAlign);
        int size = checked(samples * format.BlockAlign);
        if (position < 0 || size <= 0 || position > reader.Length - size)
            throw new InvalidDataException("Selected AF slot is incomplete.");
        reader.Position = position;
        byte[] interleaved = new byte[size];
        reader.ReadExactly(interleaved);
        if (format.Channels == 1) return interleaved;
        byte[] mono = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
            interleaved.AsSpan(i * format.BlockAlign + channel * 2, 2).CopyTo(mono.AsSpan(i * 2));
        return mono;
    }
}
