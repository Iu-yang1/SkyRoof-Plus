using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.WsjtxBridge;

public sealed record RigStatus
{
    public long RxFrequencyHz { get; init; }
    public long TxFrequencyHz { get; init; }
    public string RxMode { get; init; } = "";
    public string TxMode { get; init; } = "";
    public bool ActualPtt { get; init; }
    public string ActiveTransactionId { get; init; } = "";
    public string ActiveSessionId { get; init; } = "";
    public ulong Epoch { get; init; }
    public bool DigitalAutomationPaused { get; init; }
    public bool SupportsDuplexFrequencies { get; init; }
    public bool NativeTxAvailable { get; init; }
    public string Outcome { get; init; } = "";
}

public sealed record RigCommand(string TransactionId, DeviceOperation Operation,
    long FrequencyHz = 0, string Mode = "", string AudioSource = "");

public sealed record PcmReceipt(bool Accepted, string State);
public sealed record TxGrant(string TransactionId, ulong Epoch);

public interface IRemoteRig
{
    string SessionId { get; }
    Task<RigStatus> ReadAsync(CancellationToken ct);
    Task<RigStatus> ExecuteAsync(RigCommand command, CancellationToken ct);
    Task<PcmReceipt> SendPcmAsync(TxGrant grant, ulong sequence, byte[] pcm, CancellationToken ct);
}

public sealed class RemoteRig(HubConnection hub) : IRemoteRig
{
    public string SessionId => hub.ConnectionId ?? "";
    public Task<RigStatus> ReadAsync(CancellationToken ct) =>
        hub.InvokeAsync<RigStatus>("GetCatReadback", ct);
    public Task<RigStatus> ExecuteAsync(RigCommand command, CancellationToken ct) =>
        hub.InvokeAsync<RigStatus>("ExecuteCatCommand", command, ct);
    public Task<PcmReceipt> SendPcmAsync(TxGrant grant, ulong sequence, byte[] pcm, CancellationToken ct) =>
        hub.InvokeAsync<PcmReceipt>("PushTxPcm", grant.TransactionId, grant.Epoch, sequence, pcm, ct);
}
