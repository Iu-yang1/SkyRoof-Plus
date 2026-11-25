using System.Buffers.Binary;
using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Dsp;

public sealed partial class DspWaveformService
{
    public override Task<Q65EncodedWaveform> EncodeQ65(Q65EncodeRequest request, ServerCallContext context)
    {
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var geometry = Q65Codec.Geometry(request.Submode);
            var samples = Q65Codec.Encode(geometry, request.Message, request.AudioHz, request.Gain, 48000);
            byte[] bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
            context.CancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Q65EncodedWaveform
            {
                SlotSeconds = geometry.Seconds, TxStartSamples12K = geometry.TxStartSamples12k,
                Waveform = new EncodedWaveform
                {
                    Pcm16 = ByteString.CopyFrom(bytes), SampleRate = 48000,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                }
            });
        }
        catch (ArgumentException error) { throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message)); }
        catch (Exception error) when (error is InvalidDataException or DllNotFoundException or EntryPointNotFoundException)
        { throw new RpcException(new Status(StatusCode.FailedPrecondition, error.Message)); }
    }
}
