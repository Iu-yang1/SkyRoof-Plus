using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Supervisor;

internal static class DeviceShutdown
{
    internal static async Task<bool> ReleaseOwnedPttAsync(string pipe, CancellationToken token)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try
                {
                    await stream.ConnectAsync(2500, ct);
                    return stream;
                }
                catch
                {
                    await stream.DisposeAsync();
                    throw;
                }
            }
        };
        using var channel = GrpcChannel.ForAddress("http://localhost",
            new GrpcChannelOptions { HttpHandler = handler });
        var device = new StationDevice.StationDeviceClient(channel);
        var state = await device.ExecuteAsync(new DeviceCommand
        {
            Operation = DeviceOperation.DeviceEmergencyRx
        }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
        return !state.ActualPtt;
    }
}
