using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

public sealed partial class Q65LiveReceiver : IAsyncDisposable
{
    private sealed record Work(long Generation, Q65Geometry Geometry, Q65RxConfiguration Config, Q65Slot Slot);
    private readonly object _gate = new();
    private readonly int _channels;
    private readonly NamedSignalRing _results;
    private readonly Channel<Work> _pending = Channel.CreateBounded<Work>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _worker;
    private Q65RxConfiguration _config = new() { Submode = "60B", RxHz = 1500, ToleranceHz = 100 };
    private Q65Geometry? _geometry;
    private Q65SlotAssembler? _assembler;
    private long _generation;

    public Q65LiveReceiver(int channels)
    {
        _channels = channels;
        string name = Environment.GetEnvironmentVariable("SKYROOF_Q65_RESULTS_BUS") ?? "skyroof-q65-results";
        _results = new NamedSignalRing(name, 32, 32768, NewEpoch());
        _worker = Task.Run(DecodeAsync);
        Publish(new { state = "disabled", config = _config });
    }

    public Q65RxConfiguration Configure(Q65RxConfiguration request)
    {
        if (!float.IsFinite(request.RxHz) || request.RxHz <= 0 || request.RxHz >= 6000 ||
            !float.IsFinite(request.ToleranceHz) || request.ToleranceHz <= 0)
            throw new ArgumentException("Invalid Q65 RX frequency or tolerance.");
        Q65Geometry? geometry = request.Enabled ? Q65Codec.Geometry(request.Submode) : null;
        lock (_gate)
        {
            _config = request.Clone();
            _geometry = geometry;
            _assembler = geometry is null ? null : new Q65SlotAssembler(geometry.Seconds);
            ResetResults(request.Enabled ? "waiting-for-slot" : "disabled");
            return _config.Clone();
        }
    }

    public void Accept(SignalFrame frame)
    {
        lock (_gate)
        {
            if (_assembler is null || _geometry is null) return;
            if (_assembler.IsDiscontinuous(frame)) ResetResults("waiting-for-slot");
            DateTimeOffset first = DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(frame.MonotonicTimestampTicks)
                - TimeSpan.FromSeconds(frame.SampleCount / 48000.0);
            _assembler.Accept(frame, _channels, first, slot =>
                _pending.Writer.TryWrite(new Work(_generation, _geometry, _config, slot)));
        }
    }

    private void ResetResults(string state)
    {
        ++_generation;
        _results.ResetEpoch(NewEpoch());
        Publish(new { state, config = _config });
    }

    private void Publish(object value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (bytes.Length <= _results.MaxPayloadBytes)
            _results.Publish(0, Stopwatch.GetTimestamp(), 48000, 1, bytes);
    }

    private static ulong NewEpoch() => (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        try { await _worker; } finally { _results.Dispose(); }
    }
}
