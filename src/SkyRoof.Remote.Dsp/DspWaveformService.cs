using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Dsp;

public sealed partial class DspWaveformService(CwLiveReceiver cw, Q65LiveReceiver q65, TelemetryLiveReceiver telemetry) : DspWaveform.DspWaveformBase
{
    public override async Task<TelemetryIqReceipt> PublishTelemetryIq(IAsyncStreamReader<TelemetryIqBlock> requestStream, ServerCallContext context)
    {
        try { return await telemetry.PublishIqAsync(requestStream, context.CancellationToken); }
        catch (ArgumentException error) { throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message)); }
    }

    public override async Task<TelemetryRxConfiguration> ConfigureTelemetry(TelemetryRxConfiguration request, ServerCallContext context)
    {
        try { return await telemetry.ConfigureAsync(request, context.CancellationToken); }
        catch (ArgumentException e) { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
        catch (Exception e) when (e is InvalidDataException or DllNotFoundException or EntryPointNotFoundException)
        { throw new RpcException(new Status(StatusCode.Unavailable, e.Message)); }
    }

    public override Task<Q65RxConfiguration> ConfigureQ65(Q65RxConfiguration request, ServerCallContext context)
    {
        try { return Task.FromResult(q65.Configure(request)); }
        catch (ArgumentException e) { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidDataException)
        { throw new RpcException(new Status(StatusCode.Unavailable, e.Message)); }
    }

    public override Task<CwHistoryReply> GetCwHistory(CwHistoryRequest request, ServerCallContext context) =>
        Task.FromResult(CwHistoryReader.Read(context.CancellationToken));

    public override Task<CwRxConfiguration> ConfigureCw(CwRxConfiguration request, ServerCallContext context)
    {
        if (!Enum.TryParse<SkyRoof.CW.CwDecoderMode>(request.Mode, true, out var mode) ||
            !Enum.IsDefined(mode))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Unknown CW decoder mode."));
        cw.Configure(request.Enabled, mode);
        return Task.FromResult(request);
    }

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
