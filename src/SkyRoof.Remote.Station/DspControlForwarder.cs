using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed class DspControlForwarder : DspWaveform.DspWaveformBase, IDisposable
{
    private readonly GrpcChannel channel;
    private readonly DspWaveform.DspWaveformClient dsp;

    public DspControlForwarder()
    {
        var pipe = Environment.GetEnvironmentVariable("SKYROOF_DSP_PIPE") ?? "skyroof-station-dsp";
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var stream = LocalPipeClient.Create(pipe);
                try { await stream.ConnectAsync(3000, token); return stream; }
                catch { await stream.DisposeAsync(); throw; }
            }
        };
        channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler, MaxReceiveMessageSize = 32 * 1024 * 1024, MaxSendMessageSize = 32 * 1024 * 1024
        });
        dsp = new(channel);
    }

    public override Task<EncodedWaveform> EncodeFt4(Ft4EncodeRequest request, ServerCallContext context) =>
        dsp.EncodeFt4Async(request, deadline: context.Deadline, cancellationToken: context.CancellationToken).ResponseAsync;

    public override Task<OfflineAfDecodeResult> DecodeOfflineAf(OfflineAfDecodeRequest request, ServerCallContext context) =>
        dsp.DecodeOfflineAfAsync(request, deadline: context.Deadline, cancellationToken: context.CancellationToken).ResponseAsync;

    public override Task<Q65EncodedWaveform> EncodeQ65(Q65EncodeRequest request, ServerCallContext context) =>
        dsp.EncodeQ65Async(request, deadline: context.Deadline, cancellationToken: context.CancellationToken).ResponseAsync;

    public override Task<Q65RxConfiguration> ConfigureQ65(Q65RxConfiguration request, ServerCallContext context) =>
        dsp.ConfigureQ65Async(request, deadline: context.Deadline, cancellationToken: context.CancellationToken).ResponseAsync;

    public override Task<CwRxConfiguration> ConfigureCw(CwRxConfiguration request, ServerCallContext context) =>
        dsp.ConfigureCwAsync(request, deadline: context.Deadline, cancellationToken: context.CancellationToken).ResponseAsync;

    public override Task<CwHistoryReply> GetCwHistory(CwHistoryRequest request, ServerCallContext context) =>
        dsp.GetCwHistoryAsync(request, deadline: context.Deadline, cancellationToken: context.CancellationToken).ResponseAsync;

    public override Task<TelemetryRxConfiguration> ConfigureTelemetry(TelemetryRxConfiguration request, ServerCallContext context) =>
        dsp.ConfigureTelemetryAsync(request, deadline: context.Deadline, cancellationToken: context.CancellationToken).ResponseAsync;

    public override async Task<TelemetryIqReceipt> PublishTelemetryIq(IAsyncStreamReader<TelemetryIqBlock> request,
        ServerCallContext context)
    {
        using var call = dsp.PublishTelemetryIq(deadline: context.Deadline, cancellationToken: context.CancellationToken);
        while (await request.MoveNext(context.CancellationToken))
            await call.RequestStream.WriteAsync(request.Current, context.CancellationToken);
        await call.RequestStream.CompleteAsync();
        return await call.ResponseAsync;
    }

    public void Dispose() => channel.Dispose();
}
