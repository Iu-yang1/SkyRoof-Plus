using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed class MediaStatusBridge(IConfiguration settings) : IDisposable
{
    private readonly Lazy<GrpcChannel> _channel = new(() =>
    {
        var pipe = settings["SkyRoof:MediaPipe"] ??
            Environment.GetEnvironmentVariable("SKYROOF_MEDIA_PIPE") ??
            "skyroof-station-media";
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await stream.ConnectAsync(1000, ct); return stream; }
                catch { await stream.DisposeAsync(); throw; }
            }
        };
        return GrpcChannel.ForAddress("http://localhost",
            new GrpcChannelOptions { HttpHandler = handler });
    });

    public async Task<RxStreamReceipt> ReadAsync(CancellationToken token)
    {
        var client = new MediaIngress.MediaIngressClient(_channel.Value);
        return await client.GetRxStatusAsync(new RxStatusQuery(),
            deadline: DateTime.UtcNow.AddSeconds(3), cancellationToken: token);
    }

    public void Dispose()
    {
        if (_channel.IsValueCreated) _channel.Value.Dispose();
    }
}

public static class MediaStatusEndpoints
{
    public static void MapMediaStatus(this WebApplication app)
    {
        app.MapGet("/api/v1/media/rx-status", async (
            MediaStatusBridge bridge, CancellationToken cancellationToken) =>
        {
            try
            {
                var receipt = await bridge.ReadAsync(cancellationToken);
                return Results.Ok(new
                {
                    epoch = receipt.Epoch,
                    source = receipt.Source,
                    streamActive = receipt.StreamActive,
                    state = receipt.State,
                    framesWritten = receipt.FramesWritten,
                    samplesSkipped = receipt.SamplesSkipped,
                    datagramsReceived = receipt.DatagramsReceived,
                    datagramsOutOfOrder = receipt.DatagramsOutOfOrder,
                    datagramsDropped = receipt.DatagramsDropped,
                    datagramsMissing = receipt.DatagramsMissing
                });
            }
            catch (Exception ex) when (ex is RpcException or IOException or TimeoutException
                or OperationCanceledException)
            {
                return Results.Problem(title: "Media process unavailable",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization();
    }
}
