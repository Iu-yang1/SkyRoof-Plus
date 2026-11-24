using System.Buffers.Binary;
using System.Diagnostics;
using SkyRoof.CW;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

internal sealed class CwRemoteSession : IAsyncDisposable
{
    private readonly CwPcmIngress _ingress = new();
    private readonly CwReceiveWorker _worker;
    private readonly int _channels;
    private readonly ulong _epoch;
    private readonly DateTime _firstUtc;
    private readonly ulong _firstIndex;
    private readonly Action<object> _publish;
    public ulong Next { get; private set; }

    public CwRemoteSession(int channels, CwDecoderMode mode, SignalFrame first, Action<object> publish)
    {
        _channels = channels;
        _epoch = first.StreamEpoch;
        _firstIndex = first.FirstSampleIndex;
        Next = _firstIndex;
        _firstUtc = DateTime.UtcNow - Stopwatch.GetElapsedTime(first.MonotonicTimestampTicks)
            - TimeSpan.FromSeconds(first.SampleCount / 48000.0);
        _publish = publish;
        _ingress.Configure(true, CwReceiveAudioSource.SDR, "remote-signalbus:" + _epoch);
        string history = Environment.GetEnvironmentVariable("SKYROOF_CW_HISTORY_DIRECTORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SkyRoof", "Remote", "CW", "History");
        _worker = new CwReceiveWorker(_ingress, new CwReceiveWorkerOptions
        {
            DecoderMode = mode, MaxDecodeLanes = 8, HistoryDirectory = history
        });
        _worker.TracksUpdated += OnTracks;
        _worker.DecodeUpdated += OnDecode;
        _worker.Start();
    }

    public void Accept(SignalFrame frame)
    {
        var pcm = new float[frame.SampleCount];
        for (int i = 0; i < pcm.Length; i++)
        {
            int offset = i * _channels * 2;
            int value = BinaryPrimitives.ReadInt16LittleEndian(frame.Payload.AsSpan(offset, 2));
            if (_channels == 2)
                value = (value + BinaryPrimitives.ReadInt16LittleEndian(frame.Payload.AsSpan(offset + 2, 2))) / 2;
            pcm[i] = value / 32768f;
        }
        Next += (ulong)frame.SampleCount;
        var end = _firstUtc.AddSeconds((Next - _firstIndex) / 48000.0);
        _ingress.Append(CwReceiveAudioSource.SDR, pcm, pcm.Length, end);
    }

    private void OnTracks(object? sender, CwTracksUpdatedEventArgs e) => Publish();
    private void OnDecode(object? sender, CwDecodeUpdatedEventArgs e) => Publish();

    private static string Tail(string value) => value.Length <= 512 ? value : value[^512..];

    private void Publish()
    {
        var status = _worker.GetStatus();
        _publish(new
        {
            enabled = true, mode = _worker.DecoderMode.ToString(), state = status.ModelState.ToString(),
            sourceEpoch = _epoch, status.InferenceBusy, status.TrackingCycles,
            status.CompletedInferenceWindows, status.SkippedInferenceWindows, status.LastError,
            // Preserve lane identity when a tracker receives a new local ID.
            
            tracks = _worker.LatestTracks.OrderByDescending(t => t.SnrDb).Take(8)
                .Select(t => new { t.Id, t.FrequencyHz, t.DriftHzPerSecond,
                    t.SnrDb, t.Active, t.Confirmed, t.AssociationHintId, keying = t.KeyingStatus.ToString() }).ToArray(),
            transcripts = (_worker.LatestDecode?.Transcripts ?? []).Take(8)
                .Select(t => new { t.TrackId, t.AssociationHintId,
                    committedText = Tail(t.CommittedText), provisionalText = Tail(t.ProvisionalText),
                    t.LastWindowEndUtc }).ToArray()
        });
    }

    public async ValueTask DisposeAsync()
    {
        _worker.TracksUpdated -= OnTracks;
        _worker.DecodeUpdated -= OnDecode;
        await _worker.StopAsync();
        _worker.Dispose();
    }
}
