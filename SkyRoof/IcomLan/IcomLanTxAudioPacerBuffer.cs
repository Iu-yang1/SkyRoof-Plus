namespace SkyRoof
{
  /// <summary>
  /// Converts irregular WASAPI microphone callbacks into a bounded,
  /// sample-accurate 20-ms TX timeline. Only the transmit worker owns this
  /// buffer. Startup prefill absorbs callback jitter; missed callbacks become
  /// explicit silence instead of a burst/gap on the radio's UDP input.
  /// </summary>
  internal sealed class IcomLanTxAudioPacerBuffer
  {
    private readonly Queue<float> samples;
    private readonly int MaxSamples;
    private readonly int StartSamples;
    private readonly int SampleRate;
    private int FrameRemainder;
    private bool Started;

    internal long OverflowSamples { get; private set; }
    internal long UnderrunSamples { get; private set; }
    internal long PrefillFrames { get; private set; }
    internal long OutputFrames { get; private set; }
    internal int BufferedSamples => samples.Count;

    internal IcomLanTxAudioPacerBuffer(
      int sampleRate,
      int startupBufferMs = 60,
      int maximumBufferMs = 250)
    {
      if (sampleRate < 8000 || sampleRate > 96000)
        throw new ArgumentOutOfRangeException(nameof(sampleRate));
      if (startupBufferMs < 0 || maximumBufferMs < startupBufferMs)
        throw new ArgumentOutOfRangeException(nameof(maximumBufferMs));

      SampleRate = sampleRate;
      MaxSamples = Math.Max(1, sampleRate * maximumBufferMs / 1000);
      StartSamples = sampleRate * startupBufferMs / 1000;
      samples = new Queue<float>(MaxSamples);
    }

    internal void Push(ReadOnlySpan<float> incoming)
    {
      // Voice is real-time. Retain the newest microphone samples if Windows
      // resumes with an unexpectedly large capture burst after a pause.
      int discard = Math.Max(0, samples.Count + incoming.Length - MaxSamples);
      int fromQueue = Math.Min(discard, samples.Count);
      for (int i = 0; i < fromQueue; i++) samples.Dequeue();
      int fromIncoming = discard - fromQueue;
      OverflowSamples += discard;

      for (int i = fromIncoming; i < incoming.Length; i++)
        samples.Enqueue(incoming[i]);
    }

    internal float[] NextFrame()
    {
      int count = SampleRate / 50;
      FrameRemainder += SampleRate % 50;
      if (FrameRemainder >= 50)
      {
        count++;
        FrameRemainder -= 50;
      }

      var output = new float[count];
      OutputFrames++;
      if (!Started)
      {
        if (samples.Count < StartSamples)
        {
          PrefillFrames++;
          return output;
        }
        Started = true;
      }

      int available = Math.Min(samples.Count, count);
      for (int i = 0; i < available; i++)
        output[i] = samples.Dequeue();

      UnderrunSamples += count - available;
      return output;
    }

    internal void Reset()
    {
      samples.Clear();
      Started = false;
      FrameRemainder = 0;
    }
  }
}
