using System.Buffers.Binary;
using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.WsjtxBridge;

internal static class WsjtxRxTimelineProbe
{
    public static void Run()
    {
        var timeline = new RxPcmTimeline();
        Verify(timeline.Accept(Packet(1, 100, 1000)), true, 1000, 1000);
        Verify(timeline.Accept(Packet(1, 104, 2000)), false, 0, 0, 2000, 2000);
        if (timeline.Accept(Packet(1, 102, 999)) is not null)
            throw new InvalidDataException("Late PCM must not overwrite the sample timeline.");
        Verify(timeline.Accept(Packet(1, 105, 3000)), false, 3000);
        Verify(timeline.Accept(Packet(1, 10000, 4000)), true, 4000, 4000);
        Verify(timeline.Accept(Packet(2, 0, 5000, true)), true, 4500, 4500);
        Console.WriteLine("PASS: WSJT-X RX short gap silence, late/overlapping frame, long gap reset, epoch and stereo downmix.");
    }

    private static byte[] Packet(uint epoch, ulong index, short sample, bool stereo = false)
    {
        int channels = stereo ? 2 : 1;
        byte[] data = new byte[AudioFrameHeader.Size + 4 * channels];
        new AudioFrameHeader
        {
            Codec = 2, Flags = 0, StreamId = 1, StreamEpoch = epoch,
            Sequence = index, FirstSampleIndex = index, UtcNanoseconds = 0,
            SampleRate = 48000, SamplesPerChannel = 2, Channels = (byte)channels,
            PayloadLength = (uint)(4 * channels)
        }.Write(data);
        for (int i = 0; i < 2; i++)
        {
            int offset = AudioFrameHeader.Size + i * channels * 2;
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset), sample);
            if (stereo) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 2), (short)(sample - 1000));
        }
        return data;
    }

    private static void Verify(RxPcmBlock? block, bool reset, params short[] expected)
    {
        if (block is null || block.ResetBuffer != reset || block.Pcm.Length != expected.Length * 2)
            throw new InvalidDataException("Unexpected RX timeline block.");
        for (int i = 0; i < expected.Length; i++)
            if (BinaryPrimitives.ReadInt16LittleEndian(block.Pcm.AsSpan(i * 2)) != expected[i])
                throw new InvalidDataException("Unexpected RX timeline sample.");
    }
}
