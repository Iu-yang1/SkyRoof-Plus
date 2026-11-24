using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed class DspWaveformBridge : IDisposable
{
    private readonly Lazy<GrpcChannel> _channel = new(() =>
    {
        string pipe = Environment.GetEnvironmentVariable("SKYROOF_DSP_PIPE") ?? "skyroof-station-dsp";
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await stream.ConnectAsync(3000, ct); return stream; }
                catch { await stream.DisposeAsync(); throw; }
            }
        };
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
    });

    public async Task<EncodedWaveform> EncodeFt4Async(Ft4EncodeRequest request, CancellationToken ct) =>
        await new DspWaveform.DspWaveformClient(_channel.Value).EncodeFt4Async(request,
            deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: ct);

    public void Dispose()
    {
        if (_channel.IsValueCreated) _channel.Value.Dispose();
    }
}
