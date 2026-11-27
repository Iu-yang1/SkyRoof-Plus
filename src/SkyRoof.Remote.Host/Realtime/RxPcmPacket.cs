using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Host.Realtime;

internal static class RxPcmPacket
{
    public static byte[] Encode(SignalFrame frame, int channels)
    {
        if (frame.SampleCount is < 1 or > ushort.MaxValue ||
            frame.Payload.Length != checked(frame.SampleCount * channels * 2))
            throw new InvalidDataException("SignalBus PCM16 geometry is inconsistent.");
        var header = new AudioFrameHeader
        {
            Codec = 2, Flags = 0, StreamId = 1,
            StreamEpoch = checked((uint)frame.StreamEpoch),
            Sequence = checked((ulong)frame.Sequence), FirstSampleIndex = frame.FirstSampleIndex,
            UtcNanoseconds = 0, SampleRate = checked((uint)frame.SampleRate),
            SamplesPerChannel = checked((ushort)frame.SampleCount), Channels = (byte)channels,
            PayloadLength = checked((uint)frame.Payload.Length)
        };
        var outgoing = new byte[AudioFrameHeader.Size + frame.Payload.Length];
        header.Write(outgoing);
        frame.Payload.AsSpan().CopyTo(outgoing.AsSpan(AudioFrameHeader.Size));
        return outgoing;
    }
}
