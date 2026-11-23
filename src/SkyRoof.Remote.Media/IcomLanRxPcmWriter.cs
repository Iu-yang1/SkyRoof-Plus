using System.Buffers.Binary;
using Google.Protobuf;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Media;

/// <summary>Serializes resampled mono PCM into the 48 kHz RX SignalBus.</summary>
internal sealed class IcomLanRxPcmWriter(RxAudioPublisher publisher, ulong epoch)
{
    private readonly RxAudioPublisher _publisher = publisher;
    private readonly ulong _epoch = epoch;
    private ulong _nextOutput;

    public void SkipMissingInputSamples(ulong count, int sourceRate) =>
        _nextOutput = checked(_nextOutput + (ulong)Math.Round(
            (double)count * 48000 / sourceRate));

    public void Publish(ReadOnlySpan<float> pcm)
    {
        for (int start = 0; start < pcm.Length; start += 960)
        {
            int count = Math.Min(960, pcm.Length - start);
            var payload = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                short sample = (short)Math.Clamp(
                    (int)Math.Round(pcm[start + i] * 32768.0), -32768, 32767);
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 2, 2), sample);
            }
            _publisher.Publish(_epoch, new RxPcmChunk
            {
                FirstSampleIndex = _nextOutput,
                SampleRate = IcomLanAudioResampler.OutputRate,
                Channels = 1,
                SamplesPerChannel = count,
                Pcm16 = ByteString.CopyFrom(payload)
            });
            _nextOutput = checked(_nextOutput + (ulong)count);
        }
    }
}
