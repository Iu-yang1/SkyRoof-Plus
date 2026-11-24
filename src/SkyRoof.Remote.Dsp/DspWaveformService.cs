using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Dsp;

public sealed class DspWaveformService : DspWaveform.DspWaveformBase
{
    public override Task<EncodedWaveform> EncodeFt4(Ft4EncodeRequest request, ServerCallContext context)
    {
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            byte[] pcm = Ft4Codec.EncodePcm16(request.Message, request.AudioHz, request.Gain);
            return Task.FromResult(new EncodedWaveform
            {
                Pcm16 = ByteString.CopyFrom(pcm), SampleRate = 48000,
                Sha256 = Convert.ToHexString(SHA256.HashData(pcm))
            });
        }
        catch (ArgumentException e)
        { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
    }
}
