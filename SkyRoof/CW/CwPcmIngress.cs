using System.Diagnostics;

namespace SkyRoof.CW
{
  /// <summary>
  /// Pure source-selection and timeline gate in front of the Pileup frontend.
  /// It contains no hardware APIs, which keeps source-switch semantics fully
  /// testable in CI.
  /// </summary>
  public sealed class CwPcmIngress
  {
    public CwPileupFrontEnd FrontEnd { get; }

    /// <summary>
    /// Raised only after a valid PCM block has entered the audio hub.
    /// Subscribers must post to their own UI/message loop rather than
    /// performing signal processing on the capture callback thread.
    /// </summary>
    public event Action? SamplesAccepted;

    public bool Enabled { get; private set; }
    public CwReceiveAudioSource Source { get; private set; }
    public string? SourceIdentity { get; private set; }
    public long AcceptedSamples { get; private set; }
    public DateTime? LastAcceptedUtc { get; private set; }
    public long TimelineGeneration { get; private set; }

    private long lastAppendTimestamp;
    private long lastAppendIntervalTicks;
    private int lastBlockSamples;
    private long acceptedBlocks;

    public long AcceptedBlocks => Interlocked.Read(ref acceptedBlocks);
    public int LastBlockSamples => Volatile.Read(ref lastBlockSamples);
    public double LastPcmDeliveryIntervalMs =>
      1000.0 * Interlocked.Read(ref lastAppendIntervalTicks) /
        Stopwatch.Frequency;

    public CwPcmIngress(
      CwPileupFrontEnd? frontEnd = null)
    {
      FrontEnd =
        frontEnd ??
        new CwPileupFrontEnd();
    }

    public void Configure(
      bool enabled,
      CwReceiveAudioSource source,
      string? sourceIdentity = null)
    {
      string? normalizedIdentity =
        string.IsNullOrWhiteSpace(sourceIdentity)
          ? null
          : sourceIdentity;

      if (Enabled == enabled &&
          Source == source &&
          string.Equals(
            SourceIdentity,
            normalizedIdentity,
            StringComparison.Ordinal))
        return;

      Enabled = enabled;
      Source = source;
      SourceIdentity = normalizedIdentity;
      ResetTimeline();
    }

    public bool Append(
      CwReceiveAudioSource source,
      float[] data,
      int count,
      DateTime utc)
    {
      ArgumentNullException.ThrowIfNull(data);
      if (count < 0 || count > data.Length)
        throw new ArgumentOutOfRangeException(nameof(count));
      if (utc.Kind != DateTimeKind.Utc)
        throw new ArgumentException(
          "CW PCM timestamps must be UTC.",
          nameof(utc));
      if (!Enabled ||
          source != Source ||
          count == 0)
        return false;

      if (LastAcceptedUtc is DateTime previous &&
          utc < previous)
      {
        // The wall clock can jump backwards after NTP/manual correction.
        // Never feed a non-monotonic timestamp into CwAudioHub; reset the
        // source timeline instead of crashing an audio callback.
        ResetTimeline();
      }

      FrontEnd.AddSamples(
        data,
        count,
        utc);

      // Track actual new-audio delivery separately from FFT/GDI times.
      // On a WASAPI path with a 100-ms update cadence, no timer can paint
      // 30 unique frames/second from fresh PCM. These counters make that
      // cause visible in the CW Spectrum tooltip.
      long now = Stopwatch.GetTimestamp();
      long previousTime = Interlocked.Exchange(ref lastAppendTimestamp, now);
      if (previousTime > 0 && now > previousTime)
        Interlocked.Exchange(ref lastAppendIntervalTicks, now - previousTime);
      Volatile.Write(ref lastBlockSamples, count);
      Interlocked.Increment(ref acceptedBlocks);
      AcceptedSamples += count;
      LastAcceptedUtc = utc;
      // The waterfall display is driven by PCM arrival, not by the
      // quantized WinForms Timer. No FFT or WinForms work happens here.
      SamplesAccepted?.Invoke();
      return true;
    }

    public void ResetTimeline()
    {
      FrontEnd.Reset();
      Interlocked.Exchange(ref lastAppendTimestamp, 0);
      Interlocked.Exchange(ref lastAppendIntervalTicks, 0);
      Interlocked.Exchange(ref acceptedBlocks, 0);
      Volatile.Write(ref lastBlockSamples, 0);
      AcceptedSamples = 0;
      LastAcceptedUtc = null;
      TimelineGeneration++;
    }
  }
}
