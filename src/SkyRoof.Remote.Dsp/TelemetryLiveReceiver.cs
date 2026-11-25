using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Remote.Dsp;

public sealed partial class TelemetryLiveReceiver : IAsyncDisposable
{
    private abstract record Work;
    private sealed record Input(SignalFrame Frame, long ConfigurationVersion) : Work;
    private sealed record Configure(TelemetryRxConfiguration Config, TaskCompletionSource<TelemetryRxConfiguration> Reply) : Work;
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(64)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly NamedSignalRing _results;
    private readonly Task _worker;
    private readonly int _channels;
    private readonly TelemetryFrameArchive _archive;
    private readonly KissServer _kiss = new();
    private string _kissError = "";
    private volatile bool _enabled;
    private long _dropped;
    private ulong _epoch, _next;
    private TelemetryModem? _modem;
    private TelemetryRxConfiguration _config = new();
    private long _generation;
    private long _configurationVersion;

    public TelemetryLiveReceiver(int channels)
    {
        _channels = channels;
        _archive = new TelemetryFrameArchive(ArchiveDirectory());
        _results = new NamedSignalRing(Environment.GetEnvironmentVariable("SKYROOF_TELEMETRY_RESULTS_BUS")
            ?? "skyroof-telemetry-results", 64, 262144, (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue));
        Publish(new { state = "disabled" });
        _worker = Task.Run(RunAsync);
    }

    public void Accept(SignalFrame frame)
    {
        if (_enabled && !_queue.Writer.TryWrite(new Input(frame, Volatile.Read(ref _configurationVersion)))) Interlocked.Increment(ref _dropped);
    }

    public async Task<TelemetryRxConfiguration> ConfigureAsync(TelemetryRxConfiguration request, CancellationToken ct)
    {
        if (request.Enabled) _ = TelemetryModem.CreateSignal(request);
        if (request.KissPort is < 0 or > 65535) throw new ArgumentException("Invalid KISS TCP port.");
        var reply = new TaskCompletionSource<TelemetryRxConfiguration>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(new Configure(request.Clone(), reply), ct);
        return await reply.Task.WaitAsync(ct);
    }

    private void Publish(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (bytes.Length <= _results.MaxPayloadBytes)
            _results.Publish(0, Stopwatch.GetTimestamp(), 48000, 1, bytes);
    }

    private void FrameDecoded(Frame frame, bool inner)
    {
        if (_config.SaveFrames)
            _archive.Offer(frame, _config.NoradId, _config.Satellite, _config.Transmitter, inner);
        _kiss.SendToAll(frame, inner ? 1 : frame.CrcValid == false ? 2 : 0);
        var fields = inner ? AsrtuTmShortKissTransport.DescribeSpacePacket(frame.Bytes) :
            frame.CrcValid == true && _config.NoradId == 39444 ? Funcube1Telemetry.Describe(frame.Bytes) :
            frame.CrcValid == true && _config.NoradId == Unne1bDeframer.NoradId ? Unne1bTelemetry.Describe(frame.Bytes) :
            Enumerable.Empty<(string Name, string Value)>();
        Publish(new { state = "frame", utc = DateTimeOffset.UtcNow, generation = _generation, sourceEpoch = _epoch,
            config = _config, outputs = OutputStatus(), innerTransport = inner, hex = Convert.ToHexString(frame.Bytes),
            length = frame.Bytes.Length, crcValid = frame.CrcValid, framing = frame.Framing.ToString(),
            cfoHz = double.IsFinite(frame.CfoHz) ? (double?)frame.CfoHz : null,
            snrDb = double.IsFinite(frame.SnrDb) ? (double?)frame.SnrDb : null,
            frame.CorrectedBits, frame.ErasedBytes, frame.Note,
            fields = fields.Select(f => new { name = f.Name, value = f.Value }).ToArray() });
    }

    public async ValueTask DisposeAsync()
    {
        _enabled = false;
        _queue.Writer.TryComplete();
        try { await _worker; }
        finally
        {
            await _archive.CompleteAsync();
            await _kiss.StopAsync();
            _results.Dispose();
        }
    }
}
