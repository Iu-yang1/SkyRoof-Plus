namespace SkyRoof.CW
{
  public sealed class CwReceiveWorkerOptions
  {
    public TimeSpan TrackingInterval { get; init; } =
      TimeSpan.FromMilliseconds(120);
    public double DecodeWindowSeconds { get; init; } = 6.0;
    public double DecodeHopSeconds { get; init; } = 1.0;
    // The UI and tracker both expose eight independent CW lanes.
    // Decode all eight instead of silently leaving the last three
    // displayed lanes without ONNX inference.
    public int MaxDecodeLanes { get; init; } = 8;
    public CwDecoderMode DecoderMode {get;init;} = CwDecoderMode.DeepCW;
    public string? HistoryDirectory { get; init; }
    public double LaneBandwidthHz { get; init; } = 240;
    public double TargetCenterHz { get; init; } = 800;
    public TimeSpan ModelRetryInterval { get; init; } =
      TimeSpan.FromSeconds(5);
  }

  public enum CwReceiveModelState
  {
    Unknown,
    NotInstalled,
    Ready,
    Error
  }

  public readonly record struct CwReceiveWorkerStatus(
    bool Running,
    CwReceiveModelState ModelState,
    bool InferenceBusy,
    long TimelineGeneration,
    long TrackingCycles,
    long CompletedInferenceWindows,
    long SkippedInferenceWindows,
    int TrackCount,
    string? LastError,
    long RidgeStftCacheHits = 0,
    long RidgeStftCacheMisses = 0,
    long DeepCwStftCacheHits = 0,
    long DeepCwStftCacheMisses = 0,
    double MeanOnnxInferenceMs = 0);

  public sealed class CwTracksUpdatedEventArgs : EventArgs
  {
    public long TimelineGeneration { get; }
    public IReadOnlyList<CwSignalTrack> Tracks { get; }

    public CwTracksUpdatedEventArgs(
      long timelineGeneration,
      IReadOnlyList<CwSignalTrack> tracks)
    {
      TimelineGeneration = timelineGeneration;
      Tracks = tracks;
    }
  }

  public sealed class CwDecodeUpdatedEventArgs : EventArgs
  {
    public long TimelineGeneration { get; }
    public CwContinuousDecodeBatch Batch { get; }
    public CwDecoderMode Mode { get; }

    public CwDecodeUpdatedEventArgs(
      long timelineGeneration,
      CwContinuousDecodeBatch batch,
      CwDecoderMode mode = CwDecoderMode.DeepCW)
    {
      TimelineGeneration = timelineGeneration;
      Batch = batch;
      Mode=mode;
    }
  }

  internal interface ICwInferenceSession : IDisposable
  {
    CwContinuousDecodeBatch Decode(
      CwAudioSnapshot snapshot,
      IReadOnlyList<CwSignalTrack> tracks);

    void Reset();
  }

  internal sealed class DeepCwInferenceSession :
    ICwInferenceSession
  {
    private readonly DeepCwOnnxDecoder onnx;
    private readonly CwContinuousDeepCwDecoder continuous;

    internal long StftHits => continuous.Decoder.StftCacheHits;
    internal long StftMisses => continuous.Decoder.StftCacheMisses;
    internal double MeanOnnxInferenceMs => onnx.MeanInferenceMilliseconds;

    internal DeepCwInferenceSession(
      CwReceiveWorkerOptions options)
    {
      onnx = DeepCwOnnxDecoder.OpenInstalled();

      var decoder =
        new DeepCwMultiLaneDecoder(
          onnx.Metadata,
          onnx)
        {
          MaxLanes = options.MaxDecodeLanes,

          LaneBandwidthHz =
            options.LaneBandwidthHz,
          TargetCenterHz =
            options.TargetCenterHz
        };

      // Consecutive 6 s / 1 s ONNX windows are highly correlated.
      // Require evidence from separated windows for committed text.
      continuous =
        new CwContinuousDeepCwDecoder(
          decoder,
          new CwIncrementalTranscriptCoordinator(
            new CwTranscriptOptions
            {
              MinimumEvidenceSpanSeconds = 2.0
            }));
    }

    public CwContinuousDecodeBatch Decode(
      CwAudioSnapshot snapshot,
      IReadOnlyList<CwSignalTrack> tracks) =>
      continuous.Decode(
        snapshot.Samples,
        snapshot.SampleRate,
        snapshot.EndUtc,
        tracks,
        snapshot.EndSampleIndex);

    public void Reset() =>
      continuous.Reset();

    public void Dispose() =>
      onnx.Dispose();
  }
}
