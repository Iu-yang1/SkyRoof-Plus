namespace SkyRoof.Remote.SignalBus;

public sealed record SignalFrame(
    long Sequence,
    ulong StreamEpoch,
    ulong FirstSampleIndex,
    long MonotonicTimestampTicks,
    int SampleRate,
    int SampleCount,
    byte[] Payload);

public sealed class SignalCursor
{
    public ulong StreamEpoch { get; internal set; }
    public long NextSequence { get; internal set; }
    public long LostFrames { get; internal set; }
    public bool Initialized { get; internal set; }
}
