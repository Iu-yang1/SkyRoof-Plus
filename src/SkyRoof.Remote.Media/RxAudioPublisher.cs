using System.Diagnostics;
using System.Security.Cryptography;
using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Media;

/// <summary>
/// One timeline per stream. A new source immediately supersedes the old source;
/// the RX signal ring epoch distinguishes the discontinuity for WSS consumers.
/// </summary>
public sealed class RxAudioPublisher : IDisposable
{
    private readonly NamedSignalRing _ring;
    private readonly object _sync = new();
    private ulong _generation;
    private ulong _nextSample;
    private long _frames;
    private ulong _missingSamples;

    public RxAudioPublisher()
    {
        var name = Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_BUS")
            ?? "skyroof-rx-audio";
        _ring = new NamedSignalRing(name, 64, 4096, GenerateEpoch());
    }

    internal ulong OpenStream()
    {
        lock (_sync)
        {
            _generation = GenerateEpoch();
            _ring.ResetEpoch(_generation);
            _nextSample = 0;
            _frames = 0;
            _missingSamples = 0;
            return _generation;
        }
    }

    internal RxStreamReceipt Publish(ulong generation, RxPcmChunk chunk)
    {
        if (chunk.SampleRate != 48000 || chunk.Channels is < 1 or > 2 ||
            chunk.SamplesPerChannel is < 1 or > 960 ||
            chunk.Pcm16.Length != chunk.SamplesPerChannel * chunk.Channels * 2)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Expected PCM16 LE, 48kHz, 1-2 channels, 1-960 samples per channel."));
        lock (_sync)
        {
            if (generation != _generation)
                throw new RpcException(new Status(StatusCode.Aborted,
                    "Another RX source has taken over this station media stream."));
            if (_frames > 0 && chunk.FirstSampleIndex < _nextSample)
                throw new RpcException(new Status(StatusCode.OutOfRange,
                    "RX PCM sample index must increase without overlap."));
            if (_frames > 0) _missingSamples += chunk.FirstSampleIndex - _nextSample;
            _ring.Publish(chunk.FirstSampleIndex, Stopwatch.GetTimestamp(),
                chunk.SampleRate, chunk.SamplesPerChannel, chunk.Pcm16.ToByteArray());
            _frames++;
            _nextSample = chunk.FirstSampleIndex + (ulong)chunk.SamplesPerChannel;
            return Receipt("publishing");
        }
    }

    internal RxStreamReceipt Complete(ulong generation)
    {
        lock (_sync)
            return generation == _generation
                ? Receipt("completed")
                : new RxStreamReceipt { Epoch = generation, State = "superseded" };
    }

    private RxStreamReceipt Receipt(string state) => new()
    {
        Epoch = _generation,
        FramesWritten = _frames,
        SamplesSkipped = _missingSamples,
        State = state
    };

    private static ulong GenerateEpoch() =>
        (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    public void Dispose() => _ring.Dispose();
}
