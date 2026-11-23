using System.Buffers.Binary;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Foundation.Media;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Media;

/// <summary>
/// One authenticated Direct LAN owner's audio epoch. Session identity and
/// negotiated codec remain bound until the owner opens a new gRPC stream.
/// </summary>
internal sealed class IcomLanRxStreamDecoder(RxAudioPublisher publisher, ulong epoch)
{
    private readonly IcomLanAudioResampler _resampler = new();
    private (uint Sender, uint Receiver, int Codec, int Rate)? _identity;
    private ulong? _nextInput;
    private ulong _nextOutput;

    public void Accept(IcomRxDatagram packet)
    {
        if (packet.SampleRate is < 8_000 or > 192_000)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Icom LAN audio sample rate outside supported conversion range."));
        var identity = (packet.SenderSid, packet.ReceiverSid, packet.Codec, packet.SampleRate);
        if (_identity is not null && _identity.Value != identity)
            throw new RpcException(new Status(StatusCode.Aborted,
                "Icom LAN audio stream changed its negotiated session."));
        if (!IcomLanAudioPacketCodec.TryDecode(packet.Datagram.Span,
            packet.SenderSid, packet.ReceiverSid,
            (IcomLanAudioCodec)packet.Codec, out var frame))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Malformed Icom LAN audio frame or incorrect session ID."));

        AlignTimeline(packet.FirstSampleIndex, frame.Pcm16.Length / 2, packet.SampleRate);
        _identity = identity;
        float[] pcm = _resampler.Process(frame.Pcm16, packet.SampleRate);
        Publish(pcm);
    }

    private void AlignTimeline(ulong firstIndex, int samples, int sourceRate)
    {
        if (_nextInput is ulong expected)
        {
            if (firstIndex < expected)
                throw new RpcException(new Status(StatusCode.OutOfRange,
                    "Icom LAN RX sample index overlaps a previously published packet."));
            if (firstIndex > expected)
            {
                _nextOutput = checked(_nextOutput + (ulong)Math.Round(
                    (double)(firstIndex - expected) * IcomLanAudioResampler.OutputRate / sourceRate));
                _resampler.Reset();
            }
        }
        _nextInput = checked(firstIndex + (ulong)samples);
    }

    private void Publish(ReadOnlySpan<float> pcm)
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
            publisher.Publish(epoch, new RxPcmChunk
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
