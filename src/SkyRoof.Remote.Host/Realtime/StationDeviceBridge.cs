using System.IO.Pipes;
using System.Security.Principal;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteCatCommand(string TransactionId, DeviceOperation Operation,
    long FrequencyHz = 0, string Mode = "", string AudioSource = "");

public sealed record RemoteCatStatus(
    long RxFrequencyHz, long TxFrequencyHz, string RxMode, string TxMode,
    bool ActualPtt, string ActiveTransactionId, string ActiveSessionId,
    ulong Epoch, ulong Revision, bool DigitalAutomationPaused, string Outcome);

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
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
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

    public async Task<bool> PushTxPcmAsync(string sessionId, string transactionId,
        ulong epoch, ulong sequence, byte[] pcm, CancellationToken token)
    {
        var result = await Client.PushTxPcmAsync(new TxPcmChunk
        {
            SessionId = sessionId, TransactionId = transactionId, Epoch = epoch,
            Sequence = sequence, Pcm16 = Google.Protobuf.ByteString.CopyFrom(pcm)
        }, deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: token);
        return result.Accepted;
    }

    public void Dispose()
    {
        if (_channel.IsValueCreated) _channel.Value.Dispose();
    }

    private static RemoteCatStatus Map(DeviceSnapshot result) => new(
        result.RxFrequencyHz, result.TxFrequencyHz, result.RxMode, result.TxMode,
        result.ActualPtt, result.ActiveTransactionId, result.ActiveSessionId,
        result.TransactionEpoch, result.Revision, result.DigitalAutomationPaused,
        result.StatusText);
}
