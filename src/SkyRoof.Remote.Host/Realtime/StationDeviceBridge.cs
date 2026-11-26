using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteTxPcmReceipt(bool Accepted, string State);

public sealed record RemoteCatCommand(string TransactionId, DeviceOperation Operation,
    long FrequencyHz = 0, string Mode = "", string AudioSource = "");

public sealed record RemoteCatStatus
{
    public long RxFrequencyHz { get; init; }
    public long TxFrequencyHz { get; init; }
    public string RxMode { get; init; } = "";
    public string TxMode { get; init; } = "";
    public bool ActualPtt { get; init; }
    public string ActiveTransactionId { get; init; } = "";
    public string ActiveSessionId { get; init; } = "";
    public ulong Epoch { get; init; }
    public ulong Revision { get; init; }
    public bool DigitalAutomationPaused { get; init; }
    public string Outcome { get; init; } = "";
    public bool SupportsDuplexFrequencies { get; init; }
    public bool NativeTxAvailable { get; init; }
}

public sealed partial class StationDeviceBridge(IConfiguration settings) : IDisposable
{
    private readonly Lazy<GrpcChannel> _channel = new(() =>
    {
        var pipe = settings["SkyRoof:StationPipe"] ??
            Environment.GetEnvironmentVariable("SKYROOF_STATION_PIPE") ??
            "skyroof-station-core";
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
        return GrpcChannel.ForAddress("http://localhost",
            new GrpcChannelOptions { HttpHandler = handler });
    });

    private StationDevice.StationDeviceClient Client => new(_channel.Value);

    public async Task<RemoteCatStatus> ReadbackAsync(CancellationToken token)
    {
        try
        {
            var result = await Client.ReadbackAsync(new DeviceReadbackRequest(),
                deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
            return Map(result);
        }
        catch (RpcException ex) { throw new HubException("Device unavailable: " + ex.Status.Detail); }
    }

    public async Task<RemoteCatStatus> ExecuteAsync(
        RemoteCatCommand command, string sessionId, CancellationToken token)
    {
        try
        {
            var result = await Client.ExecuteAsync(new DeviceCommand
            {
                Operation = command.Operation,
                TransactionId = command.TransactionId,
                SessionId = sessionId,
                FrequencyHz = command.FrequencyHz,
                Mode = command.Mode,
                AudioSource = command.AudioSource
            }, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: token);
            return Map(result);
        }
        catch (RpcException ex) { throw new HubException("CAT command failed: " + ex.Status.Detail); }
    }

    public async Task ReleaseSessionAsync(string sessionId, CancellationToken token)
    {
        await Client.ReleaseSessionAsync(new DeviceReleaseSessionRequest { SessionId = sessionId },
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
    }

    public void Dispose()
    {
        if (_channel.IsValueCreated) _channel.Value.Dispose();
    }
}
