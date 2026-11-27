namespace SkyRoof.CW
{
  public sealed partial class CwReceiveWorker
  {
    public CwReceiveWorkerStatus GetStatus()
    {
      lock (sync)
      {
        return new(
          Running:
            trackingTask != null,
          ModelState:
            modelState,
          InferenceBusy:
            Volatile.Read(
              ref inferenceBusy) != 0,
          TimelineGeneration:
            observedGeneration,
          TrackingCycles:
            Interlocked.Read(
              ref trackingCycles),
          CompletedInferenceWindows:
            Interlocked.Read(
              ref completedInferenceWindows),
          SkippedInferenceWindows:
            Interlocked.Read(
              ref skippedInferenceWindows),
          TrackCount:
            latestTracks.Count,
          LastError:
            lastError,
          RidgeStftCacheHits:
            ingress.FrontEnd.FrameScanner.StftCacheHits,
          RidgeStftCacheMisses:
            ingress.FrontEnd.FrameScanner.StftCacheMisses,
          DeepCwStftCacheHits:
            (session as DeepCwInferenceSession)?.StftHits ?? 0,
          DeepCwStftCacheMisses:
            (session as DeepCwInferenceSession)?.StftMisses ?? 0,
          MeanOnnxInferenceMs:
            (session as DeepCwInferenceSession)?.MeanOnnxInferenceMs ?? 0);
      }
    }

    private void SetError(
      Exception ex)
    {
      lock (sync)
        lastError =
          ex.Message;

      OnStatusChanged();
    }

    private void OnStatusChanged() =>
      StatusChanged?.Invoke(
        this,
        EventArgs.Empty);

    private static void ValidateOptions(
      CwReceiveWorkerOptions value)
    {
      if(!Enum.IsDefined(value.DecoderMode))
        throw new ArgumentOutOfRangeException(nameof(value.DecoderMode));
      if (value.TrackingInterval <
            TimeSpan.FromMilliseconds(20) ||
          value.TrackingInterval >
            TimeSpan.FromSeconds(2))
        throw new ArgumentOutOfRangeException(
          nameof(value.TrackingInterval));
      if (!double.IsFinite(
            value.DecodeWindowSeconds) ||
          value.DecodeWindowSeconds < 1 ||
          value.DecodeWindowSeconds > 30)
        throw new ArgumentOutOfRangeException(
          nameof(value.DecodeWindowSeconds));
      if (!double.IsFinite(
            value.DecodeHopSeconds) ||
          value.DecodeHopSeconds <= 0 ||
          value.DecodeHopSeconds >
            value.DecodeWindowSeconds)
        throw new ArgumentOutOfRangeException(
          nameof(value.DecodeHopSeconds));
      if (value.MaxDecodeLanes is < 1 or > 8)
        throw new ArgumentOutOfRangeException(
          nameof(value.MaxDecodeLanes));
      if (!double.IsFinite(
            value.LaneBandwidthHz) ||
          value.LaneBandwidthHz <= 0 ||
          !double.IsFinite(
            value.TargetCenterHz) ||
          value.TargetCenterHz <= 0)
        throw new ArgumentOutOfRangeException(
          nameof(value.LaneBandwidthHz));
      if (value.ModelRetryInterval <
            TimeSpan.FromSeconds(1) ||
          value.ModelRetryInterval >
            TimeSpan.FromMinutes(5))
        throw new ArgumentOutOfRangeException(
          nameof(value.ModelRetryInterval));
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(
        disposed,
        this);
    }

    public void Dispose()
    {
      if (disposed)
        return;

      // MainForm may call Dispose on the WinForms thread while ONNX/CTC
      // inference is in native code. Cancellation is immediate, but waiting
      // for the outstanding native window is not safe on the UI thread.
      disposed=true;
      Task completion=StopAsync();
      _=completion.ContinueWith(task=>
      {
        if(task.Exception!=null)
          Serilog.Log.Error(task.Exception,
            "CW receive worker delayed shutdown failed");
        History.Dispose();
      },CancellationToken.None,
      TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
      GC.SuppressFinalize(this);
    }
  }
}
