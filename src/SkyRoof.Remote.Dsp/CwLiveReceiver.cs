using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using SkyRoof.CW;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

public sealed partial class CwLiveReceiver : IAsyncDisposable
{
    private readonly record struct Input(long Generation, CwDecoderMode Mode, SignalFrame? Frame);
    private readonly Channel<Input> _input = Channel.CreateBounded<Input>(
        new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly NamedSignalRing _results;
    private readonly int _channels;
    private readonly Task _reader;
    private readonly object _gate = new();
    private bool _enabled;
    private CwDecoderMode _mode = CwDecoderMode.DeepCW;
    private long _generation, _sessionId;
    private ulong _epoch, _next;

    public CwLiveReceiver(int channels)
    {
        _channels = channels;
        string name = Environment.GetEnvironmentVariable("SKYROOF_CW_RESULTS_BUS") ?? "skyroof-cw-results";
        _results = new NamedSignalRing(name, 16, 32768, NewEpoch());
        _reader = Task.Run(RunAsync);
        PublishState("disabled");
    }

    public void Configure(bool enabled, CwDecoderMode mode)
    {
        lock (_gate)
        {
            if (_enabled == enabled && _mode == mode) return;
            _enabled = enabled;
            _mode = mode;
            _epoch = 0;
            ++_generation;
            _results.ResetEpoch(NewEpoch());
            PublishState(enabled ? "waiting-for-audio" : "disabled");
            _input.Writer.TryWrite(new Input(_generation, mode, null));
        }
    }

    public void Accept(SignalFrame frame)
    {
        lock (_gate)
        {
            if (!_enabled) return;
            if (frame.SampleRate != 48000 || frame.SampleCount <= 0 ||
                frame.Payload.Length != frame.SampleCount * _channels * 2) return;
            if (_epoch != frame.StreamEpoch || _next != frame.FirstSampleIndex)
            {
                _epoch = frame.StreamEpoch;
                ++_generation;
                _results.ResetEpoch(NewEpoch());
                PublishState("waiting-for-audio");
            }
            _next = frame.FirstSampleIndex + (ulong)frame.SampleCount;
            _input.Writer.TryWrite(new Input(_generation, _mode, frame));
        }
    }

    private void PublishState(string state)
    {
        Publish(new { enabled = _enabled, mode = _mode.ToString(), state, sourceEpoch = _epoch,
            tracks = Array.Empty<object>(), transcripts = Array.Empty<object>() });
    }

    private void Publish(object data)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (bytes.Length <= _results.MaxPayloadBytes)
            _results.Publish(0, System.Diagnostics.Stopwatch.GetTimestamp(), 48000, 1, bytes);
    }

    private static ulong NewEpoch() => (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    public async ValueTask DisposeAsync()
    {
        _input.Writer.TryComplete();
        try { await _reader; }
        finally { _results.Dispose(); }
    }
}
