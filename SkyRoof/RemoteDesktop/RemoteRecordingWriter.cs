using NAudio.Wave;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed class RemoteRecordingWriter : IDisposable
{
    private readonly string target;
    private readonly bool iq;
    private WaveFileWriter? writer;
    private ulong epoch, next;
    private long bytes;
    private int channels, rate;
    private int segments;
    private readonly List<string> files = [];
    internal IReadOnlyList<string> Files => files;
    internal long Samples { get; private set; }
    internal double DurationSeconds { get; private set; }
    internal int Discontinuities { get; private set; }

    internal RemoteRecordingWriter(string target, bool iq)
    {
        this.target = Path.GetFullPath(target);
        this.iq = iq;
    }

    internal void Accept(StationSignalFrame frame)
    {
        int width = iq ? 4 : 2;
        if (frame.SampleRate <= 0 || (!iq && frame.SampleRate != 48000) || frame.SampleCount <= 0 || frame.Channels is < 1 or > 2 ||
            (iq && frame.Channels != 2) || frame.Payload.Length != checked(frame.SampleCount * frame.Channels * width))
            throw new InvalidDataException("Unexpected recording sample geometry.");
        bool discontinuity = writer is not null && (frame.Epoch != epoch || frame.FirstSampleIndex != next ||
            frame.Channels != channels || frame.SampleRate != rate);
        if (discontinuity) Discontinuities++;
        if (writer is null || discontinuity || bytes + frame.Payload.Length > 1_000_000_000L)
            BeginSegment(frame);
        var active = writer ?? throw new InvalidOperationException("Recording segment failed to open.");
        var data = frame.Payload.ToByteArray();
        active.Write(data, 0, data.Length);
        bytes += data.Length;
        Samples += frame.SampleCount;
        DurationSeconds += (double)frame.SampleCount / frame.SampleRate;
        next = checked(frame.FirstSampleIndex + (ulong)frame.SampleCount);
    }

    private void BeginSegment(StationSignalFrame frame)
    {
        writer?.Dispose();
        writer = null;
        string path = segments == 0 ? target :
            Path.Combine(Path.GetDirectoryName(target) ?? ".", Path.GetFileNameWithoutExtension(target) + $"-part{segments + 1:D3}.wav");
        if (File.Exists(path) && segments > 0)
            throw new IOException("Recording segment already exists: " + path);
        channels = frame.Channels;
        rate = frame.SampleRate;
        epoch = frame.Epoch;
        bytes = 0;
        var format = iq ? WaveFormat.CreateIeeeFloatWaveFormat(rate, channels) : new WaveFormat(rate, 16, channels);
        writer = new WaveFileWriter(path, format);
        files.Add(path);
        segments++;
    }

    internal void MarkQso(string id, string call)
    {
        if (files.Count == 0) return;
        File.AppendAllText(files[^1] + ".qso.jsonl", System.Text.Json.JsonSerializer.Serialize(new
        { id, call, utc = DateTimeOffset.UtcNow, samples = Samples, epoch, next }) + Environment.NewLine);
    }

    public void Dispose() { writer?.Dispose(); writer = null; }
}
