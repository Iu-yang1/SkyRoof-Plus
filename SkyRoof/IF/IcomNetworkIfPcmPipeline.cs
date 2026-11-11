namespace SkyRoof.IF
{
  /// <summary>
  /// Converts an authenticated network IF mono stream to the existing
  /// 48-kHz IF demodulator clock. The windowed sinc low-pass filters before
  /// decimation and retains phase/taps across packet boundaries.
  /// </summary>
  internal sealed class IcomNetworkIfResampler
  {
    internal const int OutputRate = 48_000;
    private const int Radius = 12;
    private readonly List<float> samples = new();
    private long bufferStart;
    private long totalSamples;
    private double nextOutputPosition;

    internal void Reset()
    {
      samples.Clear();
      bufferStart = 0;
      totalSamples = 0;
      nextOutputPosition = 0;
    }

    internal float[] Process(float[] input, int inputRate)
    {
      if (inputRate == OutputRate)
        return input;
      if (inputRate < 8_000 || inputRate > 192_000)
        throw new ArgumentOutOfRangeException(nameof(inputRate));

      samples.AddRange(input);
      totalSamples += input.Length;
      double ratio = (double)inputRate / OutputRate;
      double cutoff = Math.Min(1, (double)OutputRate / inputRate);
      var output = new List<float>(
        (int)Math.Ceiling(input.Length / ratio) + 1);

      // Delay the output by 12 input samples rather than extending packet
      // edges with zeros, which would introduce periodic phase artifacts.
      while (nextOutputPosition + Radius < totalSamples)
      {
        long center = (long)Math.Floor(nextOutputPosition);
        double weighted = 0;
        double weightSum = 0;
        for (int tap = -Radius + 1; tap <= Radius; tap++)
        {
          long index = center + tap;
          if (index < 0)
            continue;

          double distance = index - nextOutputPosition;
          double windowPosition = Math.Abs(distance) / Radius;
          if (windowPosition >= 1)
            continue;

          double sincArgument = Math.PI * distance * cutoff;
          double sinc = Math.Abs(sincArgument) < 1e-10
            ? 1 : Math.Sin(sincArgument) / sincArgument;
          double window = 0.5 * (1 + Math.Cos(Math.PI * windowPosition));
          double weight = cutoff * sinc * window;
          weighted += samples[checked((int)(index - bufferStart))] * weight;
          weightSum += weight;
        }

        output.Add((float)(weighted / weightSum));
        nextOutputPosition += ratio;
      }

      long keepFrom = Math.Max(0,
        (long)Math.Floor(nextOutputPosition) - Radius);
      int trim = (int)Math.Min(samples.Count,
        Math.Max(0, keepFrom - bufferStart));
      if (trim > 0)
      {
        samples.RemoveRange(0, trim);
        bufferStart += trim;
      }

      return output.ToArray();
    }
  }

  internal readonly record struct IcomNetworkIfBlock(
    float[] Samples, DateTime Utc, bool Discontinuity);

  /// <summary>
  /// Reorders audio frames by the UDP outer transport sequence, as used by
  /// Icom/wfview RX audio. The inner audio sequence can remain constant or
  /// advance differently and must not suppress every RX frame. Missing
  /// frames reset the IF phase rather than splicing discontinuous samples.
  /// </summary>
  internal sealed class IcomNetworkIfPcmPipeline
  {
    private const int MaxPendingFrames = 8;
    private const int MaxForwardJump = 256;
    private static readonly TimeSpan MaxReorderDelay =
      TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LinkBreak =
      TimeSpan.FromMilliseconds(500);
    private readonly Dictionary<ushort, IcomLanLpcmAudioCodec.RxFrame> pending = new();
    private readonly IcomNetworkIfResampler resampler = new();
    private ushort? nextSequence;
    private DateTime missingSince;
    private DateTime lastPacketUtc;
    private bool discontinuity = true;
    private int sourceRate;
    internal long DuplicateOrLateFrames { get; private set; }
    internal long MissingFrames { get; private set; }

    internal void Reset()
    {
      pending.Clear();
      resampler.Reset();
      nextSequence = null;
      missingSince = default;
      lastPacketUtc = default;
      sourceRate = 0;
      discontinuity = true;
    }

    internal IReadOnlyList<IcomNetworkIfBlock> Push(
      int sampleRate, IcomLanLpcmAudioCodec.RxFrame frame, DateTime utc)
    {
      if (sampleRate < 8_000 || sampleRate > 192_000)
        throw new ArgumentOutOfRangeException(nameof(sampleRate));
      if (frame.Samples.Length == 0)
        return Array.Empty<IcomNetworkIfBlock>();

      if ((sourceRate != 0 && sourceRate != sampleRate) ||
          (lastPacketUtc != default && utc - lastPacketUtc > LinkBreak))
        Reset();

      sourceRate = sampleRate;
      lastPacketUtc = utc;
      if (!nextSequence.HasValue)
        nextSequence = frame.OuterSequence;

      int ahead = (ushort)(frame.OuterSequence - nextSequence.Value);
      if (ahead >= 0x8000)
      {
        DuplicateOrLateFrames++;
        return Array.Empty<IcomNetworkIfBlock>();
      }
      if (ahead > MaxForwardJump)
      {
        Reset();
        sourceRate = sampleRate;
        nextSequence = frame.OuterSequence;
        lastPacketUtc = utc;
      }
      if (!pending.TryAdd(frame.OuterSequence, frame))
      {
        DuplicateOrLateFrames++;
        return Array.Empty<IcomNetworkIfBlock>();
      }

      return Drain(utc);
    }

    internal IReadOnlyList<IcomNetworkIfBlock> Flush(DateTime utc) =>
      Drain(utc);

    private IReadOnlyList<IcomNetworkIfBlock> Drain(DateTime utc)
    {
      var result = new List<IcomNetworkIfBlock>();
      while (nextSequence.HasValue)
      {
        if (pending.Remove(nextSequence.Value, out var ready))
        {
          float[] converted = resampler.Process(ready.Samples, sourceRate);
          if (converted.Length > 0)
          {
            result.Add(new IcomNetworkIfBlock(
              converted, utc, discontinuity));
            discontinuity = false;
          }

          nextSequence = unchecked((ushort)(nextSequence.Value + 1));
          missingSince = default;
          continue;
        }

        if (pending.Count == 0)
          break;

        if (missingSince == default)
          missingSince = utc;

        if (pending.Count < MaxPendingFrames &&
            utc - missingSince < MaxReorderDelay)
          break;

        ushort nextAvailable = pending.Keys.MinBy(
          sequence => (ushort)(sequence - nextSequence.Value));
        MissingFrames += (ushort)(nextAvailable - nextSequence.Value);
        nextSequence = nextAvailable;
        resampler.Reset();
        discontinuity = true;
        missingSince = default;
      }

      return result;
    }
  }
}
