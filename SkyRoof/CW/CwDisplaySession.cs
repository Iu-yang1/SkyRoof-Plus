namespace SkyRoof.CW
{
  /// <summary>
  /// Owns the display-only CW FFT and optional HamNoise pipeline. A console
  /// subscribes to completed immutable frames, not native FFT resources.
  /// This session never accesses the CW receive decoder or CAT transmitter.
  /// </summary>
  internal sealed class CwDisplaySession : IDisposable
  {
    private readonly CwDisplayFrameProcessor processor;

    internal double MinimumFrequencyHz { get; }
    internal double MaximumFrequencyHz { get; }
    internal double RequiredAudioSeconds { get; }

    internal bool Busy => processor.Busy;

    internal event Action? FrameReady
    {
      add => processor.FrameReady += value;
      remove => processor.FrameReady -= value;
    }

    internal CwDisplaySession(
      int sampleRate,
      double minimumFrequencyHz,
      double maximumFrequencyHz)
    {
      var waterfall = new CwAudioWaterfallAnalyzer(
        sampleRate: sampleRate,
        fftSize: 2048,
        minFrequencyHz: minimumFrequencyHz,
        maxFrequencyHz: maximumFrequencyHz,
        outputBins: 512);
      var spectrum = new CwAudioWaterfallAnalyzer(
        sampleRate: sampleRate,
        fftSize: 8192,
        minFrequencyHz: minimumFrequencyHz,
        maxFrequencyHz: maximumFrequencyHz,
        outputBins: 512);

      processor = new CwDisplayFrameProcessor(waterfall, spectrum);
      MinimumFrequencyHz = spectrum.MinFrequencyHz;
      MaximumFrequencyHz = spectrum.MaxFrequencyHz;
      RequiredAudioSeconds = Math.Max(
        0.20,
        spectrum.FftSize / (double)spectrum.SampleRate + 0.02);
    }

    internal bool TryQueue(
      CwAudioSnapshot snapshot,
      long timelineGeneration,
      CwDenoiseMode mode) =>
      processor.TryQueue(snapshot, timelineGeneration, mode);

    internal bool TryTake(out CwDisplayFrameResult result) =>
      processor.TryTake(out result);

    internal void RecordBusyTick() =>
      processor.RecordBusyTick();

    public void Dispose() => processor.Dispose();
  }
}
