using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient : IDisposable
{
    private readonly GrpcChannel channel;
    private readonly StationDevice.StationDeviceClient cat;
    private readonly DeviceRotator.DeviceRotatorClient rotator;
    private readonly StationTrackingJobs.StationTrackingJobsClient tracking;
    private readonly DspWaveform.DspWaveformClient dsp;
    private readonly StationSignals.StationSignalsClient signals;
    private readonly StationCatalog.StationCatalogClient catalog;
    private readonly StationTracking.StationTrackingClient predictor;
    private readonly string session = "desktop-" + Guid.NewGuid().ToString("N");

    internal StationIpcClient(string pipe)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var stream = LocalPipeClient.Create(pipe);
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
        channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler, MaxReceiveMessageSize = 32 * 1024 * 1024, MaxSendMessageSize = 32 * 1024 * 1024 });
        cat = new(channel);
        rotator = new(channel);
        tracking = new(channel);
        dsp = new(channel);
        signals = new(channel);
        catalog = new(channel);
        predictor = new(channel);
    }

    internal Task<DeviceSnapshot> ReadRadioAsync(CancellationToken token) =>
        cat.ReadbackAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;

    internal Task<DeviceSnapshot> SetRadioAsync(DeviceOperation operation, long frequency, string mode, CancellationToken token) =>
        cat.ExecuteAsync(new DeviceCommand
        {
            TransactionId = Guid.NewGuid().ToString("N"), SessionId = session,
            Operation = operation, FrequencyHz = frequency, Mode = mode
        }, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: token).ResponseAsync;

    internal Task<RotatorPosition> ReadRotatorAsync(CancellationToken token) =>
        rotator.ReadAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;

    internal Task<RotatorPosition> PointAsync(double az, double el, CancellationToken token) =>
        rotator.PointAsync(new() { AzimuthDegrees = az, ElevationDegrees = el },
            deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: token).ResponseAsync;

    internal Task<RotatorPosition> StopAsync(CancellationToken token) =>
        rotator.StopAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;

    internal Task<TrackingJobStatus> TrackingAsync(CancellationToken token) =>
        tracking.StatusAsync(new(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync;

    public void Dispose() => channel.Dispose();
}
