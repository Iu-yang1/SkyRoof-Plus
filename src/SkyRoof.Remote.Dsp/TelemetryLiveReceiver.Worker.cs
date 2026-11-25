using System.Buffers.Binary;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

public sealed partial class TelemetryLiveReceiver
{
    private async Task RunAsync()
    {
        try
        {
            await foreach (var work in _queue.Reader.ReadAllAsync())
            {
                if (work is Configure configure)
                {
                    try { Apply(configure); }
                    catch (Exception error) { configure.Reply.TrySetException(error); }
                }
                else if (work is Input input && _modem is not null)
                {
                    try { Process(input.Frame); }
                    catch (Exception error)
                    {
                        _enabled = false;
                        _modem?.Dispose();
                        _modem = null;
                        Publish(new { state = "error", config = _config, error = error.Message });
                    }
                }
            }
            _modem?.Flush();
        }
        finally { _modem?.Dispose(); }
    }

    private void Apply(Configure work)
    {
        // Build first so an invalid profile cannot tear down a working receiver.
        var next = work.Config.Enabled ? new TelemetryModem(work.Config, FrameDecoded) : null;
        try
        {
            _modem?.Flush();
            _modem?.Dispose();
        }
        catch { next?.Dispose(); throw; }
        _modem = next;
        _config = work.Config;
        ++_generation;
        _epoch = 0;
        _next = 0;
        _enabled = _modem is not null;
        Publish(new { state = _enabled ? "receiving" : "disabled", config = _config,
            generation = _generation, droppedBlocks = Interlocked.Read(ref _dropped) });
        work.Reply.TrySetResult(_config.Clone());
    }

    private void Process(SignalFrame frame)
    {
        if (frame.SampleRate != 48000 || frame.Payload.Length != frame.SampleCount * _channels * 2)
            throw new InvalidDataException("Incorrect telemetry PCM geometry.");
        if (_epoch != frame.StreamEpoch || _next != frame.FirstSampleIndex)
        {
            if (_epoch != 0)
            {
                _modem?.Dispose();
                _modem = new TelemetryModem(_config, FrameDecoded);
                ++_generation;
                Publish(new { state = "discontinuity", config = _config, generation = _generation,
                    droppedBlocks = Interlocked.Read(ref _dropped) });
            }
            _epoch = frame.StreamEpoch;
        }
        _next = frame.FirstSampleIndex + (ulong)frame.SampleCount;
        var pcm = new float[frame.SampleCount];
        for (int i = 0; i < pcm.Length; i++)
        {
            int offset = i * _channels * 2;
            int value = BinaryPrimitives.ReadInt16LittleEndian(frame.Payload.AsSpan(offset));
            if (_channels == 2) value += BinaryPrimitives.ReadInt16LittleEndian(frame.Payload.AsSpan(offset + 2));
            pcm[i] = value / (32768f * _channels);
        }
        _modem!.Push(pcm);
    }
}
