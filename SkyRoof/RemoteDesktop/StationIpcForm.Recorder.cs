using Google.Protobuf;
using Grpc.Core;
using NAudio.Wave;
using NAudio.MediaFoundation;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly CheckBox recordIq = new() { Text = "Telemetry CF32 I/Q (not AF)", AutoSize = true };
    private readonly Label recordStatus = new() { AutoSize = true, MaximumSize = new Size(750, 0) };
    private readonly ListBox recordFiles = new() { Width = 750, Height = 230 };
    private CancellationTokenSource? recordingSource, replaySource;
    private RemoteRecordingWriter? recordingWriter;
    private WaveOutEvent? filePlayback;
    private AudioFileReader? playbackFile;

    private TabPage CreateRecorderPage()
    {
        var page = new TabPage("RECORDER");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };
        layout.Controls.Add(Row(recordIq, ActionButton("RECORD WAV", RecordAsync), ActionButton("STOP RECORD", () => StopStream(recordingSource))));
        layout.Controls.Add(recordStatus);
        layout.Controls.Add(recordFiles);
        layout.Controls.Add(Row(ActionButton("LOAD WAV", LoadRecordingAsync), ActionButton("PLAY AF", PlayRecordingAsync),
            ActionButton("STOP PLAY", StopFilePlaybackAsync), ActionButton("EXPORT MP3", ExportMp3Async)));
        layout.Controls.Add(Row(ActionButton("REPLAY IQ TO DSP", ReplayIqAsync), ActionButton("STOP REPLAY", () => StopStream(replaySource))));
        layout.Controls.Add(Row("Recordings saved on this desktop; gaps/epoch changes start a new WAV segment."));
        layout.Controls.Add(Row("I/Q replay uses configured Station telemetry receiver, not the speaker or TX."));
        page.Controls.Add(layout);
        return page;
    }

    private async Task RecordAsync()
    {
        if (recordingSource is not null) return;
        bool iq = recordIq.Checked;
        using var dialog = new SaveFileDialog { Filter = "Waveform|*.wav", FileName = $"skyroof-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{(iq ? "iq" : "af")}.wav" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        recordingSource = source;
        using var writer = new RemoteRecordingWriter(dialog.FileName, iq);
        recordingWriter = writer;
        recordStatus.Text = "Waiting for actual samples...";
        try
        {
            var kind = iq ? StationSignalKind.StationSignalIqCf32 : StationSignalKind.StationSignalRxAudio;
            using var call = client.Subscribe(kind, source.Token);
            while (await call.ResponseStream.MoveNext(source.Token))
            {
                writer.Accept(call.ResponseStream.Current);
                if (!IsDisposed) recordStatus.Text = $"{writer.Samples / 48000.0:F1} s · {writer.Files.Count} files · {writer.Discontinuities} gaps";
            }
        }
        catch (RpcException error) when (source.IsCancellationRequested && error.StatusCode == StatusCode.Cancelled)
        { recordStatus.Text = "Recording stopped."; }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        { recordStatus.Text = "Recording stopped."; }
        finally
        {
            recordingSource = null; recordingWriter = null;
            if (!IsDisposed)
            {
                foreach (var path in writer.Files) recordFiles.Items.Add(path);
                recordStatus.Text = $"Saved {writer.Samples} samples · {writer.Files.Count} files · {writer.Discontinuities} gaps";
            }
        }
    }

    private Task LoadRecordingAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Waveform|*.wav" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            recordFiles.Items.Add(dialog.FileName);
            recordFiles.SelectedIndex = recordFiles.Items.Count - 1;
        }
        return Task.CompletedTask;
    }

    private string SelectedRecording() => recordFiles.SelectedItem as string ?? throw new InvalidOperationException("Select a recording.");
    private static bool IsIq(WaveFormat format) => format.Encoding == WaveFormatEncoding.IeeeFloat && format.Channels == 2 && format.BitsPerSample == 32;

    private Task PlayRecordingAsync()
    {
        string path = SelectedRecording();
        using var header = new WaveFileReader(path);
        if (IsIq(header.WaveFormat)) throw new InvalidOperationException("Use IQ replay for a CF32 I/Q recording.");
        StopFilePlayback();
        playbackFile = new AudioFileReader(path);
        filePlayback = new WaveOutEvent();
        filePlayback.Init(playbackFile);
        filePlayback.Play();
        recordStatus.Text = "Playing " + Path.GetFileName(path);
        return Task.CompletedTask;
    }

    private Task StopFilePlaybackAsync() { StopFilePlayback(); return Task.CompletedTask; }
    private void StopFilePlayback()
    {
        filePlayback?.Dispose(); filePlayback = null;
        playbackFile?.Dispose(); playbackFile = null;
    }

    private async Task ExportMp3Async()
    {
        string path = SelectedRecording();
        using var dialog = new SaveFileDialog { Filter = "MP3|*.mp3", FileName = Path.GetFileNameWithoutExtension(path) + ".mp3" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Task.Run(() =>
        {
            using var reader = new WaveFileReader(path);
            if (IsIq(reader.WaveFormat)) throw new InvalidOperationException("I/Q cannot be exported as MP3.");
            using var resampled = new MediaFoundationResampler(reader, 16000) { ResamplerQuality = 60 };
            MediaFoundationEncoder.EncodeToMp3(resampled, dialog.FileName, 24000);
        });
        recordStatus.Text = "MP3 exported.";
    }

    private async Task ReplayIqAsync()
    {
        if (replaySource is not null) return;
        using var reader = new WaveFileReader(SelectedRecording());
        if (!IsIq(reader.WaveFormat) || reader.WaveFormat.SampleRate != 48000)
            throw new InvalidDataException("IQ replay requires 48 kHz stereo IEEE float WAV.");
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        replaySource = source;
        try
        {
            var receipt = await client.ReplayIqAsync(reader, source.Token);
            recordStatus.Text = $"DSP accepted {receipt.Samples} complex samples.";
        }
        finally { replaySource = null; }
    }
}
