using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Station;

public sealed class StationSignalService : StationSignals.StationSignalsBase
{
    private sealed record RingSpec(string Name, int Slots, int Capacity, int Channels = 0);

    public override async Task Subscribe(StationSignalRequest request,
        IServerStreamWriter<StationSignalFrame> response, ServerCallContext context)
    {
        var spec = Resolve(request.Kind);
        var token = context.CancellationToken;
        while (!token.IsCancellationRequested)
        {
            using var ring = TryOpen(spec);
            if (ring is null)
            {
                await Task.Delay(200, token);
                continue;
            }
            await ReadAsync(ring, spec, response, token);
        }
    }

    private static NamedSignalRing? TryOpen(RingSpec spec)
    {
        try { return new NamedSignalRing(spec.Name, spec.Slots, spec.Capacity); }
        catch (InvalidDataException) { return null; }
    }

    private static async Task ReadAsync(NamedSignalRing ring, RingSpec spec,
        IServerStreamWriter<StationSignalFrame> response, CancellationToken token)
    {
        var cursor = new SignalCursor();
        while (!token.IsCancellationRequested)
        {
            SignalFrame? frame;
            try
            {
                if (!ring.TryReadNext(cursor, out frame) || frame is null)
                {
                    await Task.Delay(15, token);
                    continue;
                }
            }
            catch (InvalidDataException) { return; }
            await response.WriteAsync(new StationSignalFrame
            {
                Sequence = frame.Sequence, Epoch = frame.StreamEpoch,
                FirstSampleIndex = frame.FirstSampleIndex, MonotonicTicks = frame.MonotonicTimestampTicks,
                SampleRate = frame.SampleRate, SampleCount = frame.SampleCount,
                Payload = ByteString.CopyFrom(frame.Payload), LostFrames = cursor.LostFrames,
                Channels = spec.Channels
            }, token);
        }
    }

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable("SKYROOF_" + key) ?? fallback;

    private static RingSpec Resolve(StationSignalKind kind) => kind switch
    {
        StationSignalKind.StationSignalRxAudio => new(Env("RX_AUDIO_BUS", "skyroof-rx-audio"), 64, 4096,
            int.TryParse(Env("RX_AUDIO_CHANNELS", "1"), out var channels) && channels == 2 ? 2 : 1),
        StationSignalKind.StationSignalAfSpectrum => new(Env("AF_SPECTRUM_BUS", "skyroof-af-spectrum"), 64, 4096),
        StationSignalKind.StationSignalRfMain => new(Env("RF_SCOPE_BUS", "skyroof-rf-scope") + "-main", 64, 4096),
        StationSignalKind.StationSignalRfSub => new(Env("RF_SCOPE_BUS", "skyroof-rf-scope") + "-sub", 64, 4096),
        StationSignalKind.StationSignalFt4 => new(Env("FT4_RESULTS_BUS", "skyroof-ft4-results"), 32, 32768),
        StationSignalKind.StationSignalCw => new(Env("CW_RESULTS_BUS", "skyroof-cw-results"), 16, 32768),
        StationSignalKind.StationSignalQ65 => new(Env("Q65_RESULTS_BUS", "skyroof-q65-results"), 32, 32768),
        StationSignalKind.StationSignalTelemetry => new(Env("TELEMETRY_RESULTS_BUS", "skyroof-telemetry-results"), 64, 262144),
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Unknown station signal."))
    };
}
