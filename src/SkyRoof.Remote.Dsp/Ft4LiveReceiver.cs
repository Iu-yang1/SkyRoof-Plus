using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

internal sealed class Ft4LiveReceiver : IDisposable
{
    private readonly int _channels;
    private readonly double _delayMs;
    private readonly NamedSignalRing _results;
    private readonly Ft4SlotAssembler _assembler = new();
    private readonly Channel<Ft4SlotWindow> _pending = Channel.CreateBounded<Ft4SlotWindow>(
        new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _decoder;
    private ulong _sourceEpoch;
    private readonly object _gate = new();

    public Ft4LiveReceiver(int channels)
    {
        _channels = channels;
        _delayMs = double.TryParse(Environment.GetEnvironmentVariable("SKYROOF_FT4_RX_DELAY_MS"),
            System.Globalization.CultureInfo.InvariantCulture, out double delay) && double.IsFinite(delay) ? delay : 0;
        string name = Environment.GetEnvironmentVariable("SKYROOF_FT4_RESULTS_BUS") ?? "skyroof-ft4-results";
        _results = new NamedSignalRing(name, 32, 32768, NewEpoch());
        _decoder = Task.Run(DecodeAsync);
    }

    public void Accept(SignalFrame frame)
    {
        lock (_gate)
        {
            if (_sourceEpoch != frame.StreamEpoch)
            {
                _sourceEpoch = frame.StreamEpoch;
                _results.ResetEpoch(NewEpoch());
            }
        }
        // Publish time is a measured local anchor, not an ADC UTC timestamp.
        DateTime first = DateTime.UtcNow - Stopwatch.GetElapsedTime(frame.MonotonicTimestampTicks)
            - TimeSpan.FromSeconds(frame.SampleCount / 48000.0) - TimeSpan.FromMilliseconds(_delayMs);
        _assembler.Accept(frame, _channels, first, window => _pending.Writer.TryWrite(window));
    }

    private async Task DecodeAsync()
    {
        await foreach (var window in _pending.Reader.ReadAllAsync())
        {
            lock (_gate) { if (window.SourceEpoch != _sourceEpoch) continue; }
            try
            {
                string[] messages = Ft4Codec.Decode(window.Samples);
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    utc = new DateTime(window.SlotUtcTicks, DateTimeKind.Utc),
                    messages, sourceEpoch = window.SourceEpoch,
                    rxDelayMs = _delayMs, timing = "estimated-publish-anchor"
                });
                lock (_gate)
                {
                    if (window.SourceEpoch == _sourceEpoch && payload.Length <= _results.MaxPayloadBytes)
                        _results.Publish(0, Stopwatch.GetTimestamp(), 48000, 1, payload);
                }
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or InvalidDataException)
            { Console.Error.WriteLine("FT4 decoder: " + error.Message); }
        }
    }

    private static ulong NewEpoch() => (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    public void Dispose()
    {
        _pending.Writer.TryComplete();
        _decoder.GetAwaiter().GetResult();
        _results.Dispose();
    }
}
