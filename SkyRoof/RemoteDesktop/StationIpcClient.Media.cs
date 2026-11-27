using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal AsyncServerStreamingCall<StationSignalFrame> Subscribe(StationSignalKind kind, CancellationToken token) =>
        signals.Subscribe(new StationSignalRequest { Kind = kind }, cancellationToken: token);

    internal Task<TxPcmReceipt> PushPcmAsync(string transaction, ulong epoch, ulong sequence, byte[] pcm, CancellationToken token) =>
        cat.PushTxPcmAsync(new TxPcmChunk
        {
            TransactionId = transaction, SessionId = session, Epoch = epoch,
            Sequence = sequence, Pcm16 = ByteString.CopyFrom(pcm)
        }, deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: token).ResponseAsync;

    internal Task<CwRxConfiguration> ConfigureCwAsync(CwRxConfiguration config, CancellationToken token) =>
        dsp.ConfigureCwAsync(config, deadline: DateTime.UtcNow.AddSeconds(8), cancellationToken: token).ResponseAsync;

    internal Task<CwHistoryReply> ReadCwHistoryAsync(CancellationToken token) =>
        dsp.GetCwHistoryAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;

    internal Task<Q65RxConfiguration> ConfigureQ65Async(Q65RxConfiguration config, CancellationToken token) =>
        dsp.ConfigureQ65Async(config, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;

    internal Task<TelemetryRxConfiguration> ConfigureTelemetryAsync(TelemetryRxConfiguration config, CancellationToken token) =>
        dsp.ConfigureTelemetryAsync(config, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: token).ResponseAsync;

    internal Task<EncodedWaveform> EncodeFt4Async(Ft4EncodeRequest request, CancellationToken token) =>
        dsp.EncodeFt4Async(request, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: token).ResponseAsync;

    internal Task<Q65EncodedWaveform> EncodeQ65Async(Q65EncodeRequest request, CancellationToken token) =>
        dsp.EncodeQ65Async(request, deadline: DateTime.UtcNow.AddSeconds(35), cancellationToken: token).ResponseAsync;

    internal Task<TimedTxReceipt> TimedTxAsync(EncodedWaveform waveform, long hz, long target, CancellationToken token) =>
        cat.RunTimedTxAsync(new TimedTxRequest
        {
            JobId = Guid.NewGuid().ToString("N"), SessionId = session, AuthorizedPrincipal = session,
            FrequencyHz = hz, Mode = "USB-D", TargetUtcNanoseconds = target,
            Pcm16 = waveform.Pcm16, Sha256 = waveform.Sha256
        }, deadline: DateTime.UtcNow.AddMinutes(16), cancellationToken: token).ResponseAsync;
}
