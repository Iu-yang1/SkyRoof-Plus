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
    private readonly IcomLanRxPcmWriter _writer = new(publisher, epoch);
    private readonly IcomLanPacketReorderBuffer _reorder = new();
    private (uint Sender, uint Receiver, int Codec, int Rate)? _identity;
    private ulong? _nextInput;

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

        _identity = identity;
        var ready = _reorder.Push(new IcomLanOrderedRxPacket(
            frame.OuterSequence, packet.FirstSampleIndex, packet.SampleRate, frame.Pcm16),
            DateTime.UtcNow);
        foreach (var item in ready) AcceptOrdered(item);
    }

    public void FlushDue()
    {
        foreach (var item in _reorder.Flush(DateTime.UtcNow))
            AcceptOrdered(item);
    }

    public void Complete()
    {
        foreach (var item in _reorder.Flush(DateTime.UtcNow, force: true))
            AcceptOrdered(item);
    }

    private void AcceptOrdered(IcomLanOrderedRxPacket packet)
    {
        AlignTimeline(packet.FirstSampleIndex, packet.Pcm16.Length / 2, packet.SampleRate);
        float[] pcm = _resampler.Process(packet.Pcm16, packet.SampleRate);
        _writer.Publish(pcm);
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
                _writer.SkipMissingInputSamples(firstIndex - expected, sourceRate);
                _resampler.Reset();
            }
        }
        _nextInput = checked(firstIndex + (ulong)samples);
    }
}
