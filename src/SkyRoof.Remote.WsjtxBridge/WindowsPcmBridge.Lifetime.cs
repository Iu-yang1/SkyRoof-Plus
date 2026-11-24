using NAudio.Wave;

namespace SkyRoof.Remote.WsjtxBridge;

internal sealed partial class WindowsPcmBridge
{
    public async Task RunAsync(Uri station, string token, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stopped = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, StoppedEventArgs args) =>
            stopped.TrySetResult(args.Exception ?? new IOException("Virtual audio device stopped unexpectedly."));
        _capture.RecordingStopped += OnStopped;
        _playback.PlaybackStopped += OnStopped;
        Task[] tasks = [];
        bool captureStarted = false;
        bool playbackStarted = false;
        try
        {
            _playback.Play();
            playbackStarted = true;
            _capture.StartRecording();
            captureStarted = true;
            tasks = [ReceiveAsync(station, token, lifetime.Token), SendAsync(lifetime.Token)];
            Task completed = await Task.WhenAny(tasks.Append(stopped.Task));
            if (completed == stopped.Task) throw await stopped.Task;
            await completed;
        }
        finally
        {
            lifetime.Cancel();
            _capture.RecordingStopped -= OnStopped;
            _playback.PlaybackStopped -= OnStopped;
            if (captureStarted) _capture.StopRecording();
            if (playbackStarted) _playback.Stop();
            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }
}
