using System.Diagnostics;
using System.Security.Cryptography;
using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

public sealed partial class TelemetryLiveReceiver
{
    internal async Task<TelemetryIqReceipt> PublishIqAsync(IAsyncStreamReader<TelemetryIqBlock> source, CancellationToken ct)
    {
        if (!await _iqLease.WaitAsync(0, ct))
            throw new RpcException(new Status(StatusCode.AlreadyExists, "An IQ producer already owns the receiver."));
        try
        {
            long version = Volatile.Read(ref _configurationVersion);
            CheckIqVersion(version);
            ulong epoch = (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            var receipt = new TelemetryIqReceipt { SourceEpoch = epoch };
            while (await source.MoveNext(ct))
            {
                CheckIqVersion(version);
                var block = source.Current;
                if (block.SampleRate != 48000 || block.Cf32.Length is < 8 or > 32768 || block.Cf32.Length % 8 != 0)
                    throw new ArgumentException("IQ ingress expects 48 kHz interleaved little-endian CF32, 1–4096 complex samples.");
                int samples = block.Cf32.Length / 8;
                var frame = new SignalFrame(receipt.Blocks, epoch, block.FirstSampleIndex, Stopwatch.GetTimestamp(),
                    block.SampleRate, samples, block.Cf32.ToByteArray());
                await _queue.Writer.WriteAsync(new Input(frame, version, true), ct);
                receipt.Blocks++;
                receipt.Samples += samples;
            }
            var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _queue.Writer.WriteAsync(new FinishIq(version, reply), ct);
            await reply.Task.WaitAsync(ct);
            return receipt;
        }
        finally { _iqLease.Release(); }
    }

    private void CheckIqVersion(long version)
    {
        if (!_enabled || !_iqEnabled || version != Volatile.Read(ref _configurationVersion))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Configure IQ receiver before streaming; profile changed or disabled."));
    }

    private async Task FinishIqAsync(FinishIq work)
    {
        try
        {
            CheckIqVersion(work.ConfigurationVersion);
            (_modem ?? throw new InvalidOperationException("IQ modem unavailable.")).Flush();
            await _archive.FlushAsync();
            Publish(new { state = "iq-source-ended", config = _config, generation = _generation, outputs = OutputStatus() });
            work.Reply.TrySetResult();
        }
        catch (Exception error) { work.Reply.TrySetException(error); }
    }
}
