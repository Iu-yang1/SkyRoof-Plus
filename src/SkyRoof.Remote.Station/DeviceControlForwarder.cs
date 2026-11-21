using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

/// <summary>
/// Station owns the command-plane entry point. Only Device talks to CAT
/// hardware; this proxy never retries non-idempotent TX commands.
/// </summary>
public sealed class DeviceControlForwarder : StationDevice.StationDeviceBase, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly StationDevice.StationDeviceClient _device;

    public DeviceControlForwarder()
    {
        var devicePipe = Environment.GetEnvironmentVariable("SKYROOF_DEVICE_PIPE")
            ?? "skyroof-station-device";
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var stream = new NamedPipeClientStream(".", devicePipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try
                {
                    await stream.ConnectAsync(3000, token);
                    return stream;
                }
                catch
                {
                    await stream.DisposeAsync();
                    throw;
                }
            }
        };
        _channel = GrpcChannel.ForAddress("http://localhost",
            new GrpcChannelOptions { HttpHandler = handler });
        _device = new StationDevice.StationDeviceClient(_channel);
    }

    public override async Task<DeviceSnapshot> Execute(
        DeviceCommand request, ServerCallContext context) =>
        await _device.ExecuteAsync(request,
            deadline: DateTime.UtcNow.AddSeconds(8),
            cancellationToken: context.CancellationToken);

    public override async Task<DeviceSnapshot> Readback(
        DeviceReadbackRequest request, ServerCallContext context) =>
        await _device.ReadbackAsync(request,
            deadline: DateTime.UtcNow.AddSeconds(5),
            cancellationToken: context.CancellationToken);

    public override async Task<DeviceSnapshot> ReleaseSession(
        DeviceReleaseSessionRequest request, ServerCallContext context) =>
        await _device.ReleaseSessionAsync(request,
            deadline: DateTime.UtcNow.AddSeconds(5),
            cancellationToken: context.CancellationToken);

    public void Dispose() => _channel.Dispose();
}
