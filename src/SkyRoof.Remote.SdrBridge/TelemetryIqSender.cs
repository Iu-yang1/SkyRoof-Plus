using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.SdrBridge;

internal sealed class TelemetryIqSender : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly DspWaveform.DspWaveformClient _client;
    private AsyncClientStreamingCall<TelemetryIqBlock, TelemetryIqReceipt>? _call;
    private ulong _index;

    internal TelemetryIqSender(string pipe)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await stream.ConnectAsync(5000, ct); return stream; }
                catch { await stream.DisposeAsync(); throw; }
            }
        };
        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        _client = new DspWaveform.DspWaveformClient(_channel);
    }

    internal async Task SendAsync(float[] data, CancellationToken ct)
    {
        if (data.Length == 0) return;
        var call = _call ??= _client.PublishTelemetryIq(cancellationToken: ct);
        for (int offset = 0; offset < data.Length; offset += 8192)
        {
            int count = Math.Min(8192, data.Length - offset);
            var bytes = ByteString.CopyFrom(MemoryMarshal.AsBytes(data.AsSpan(offset, count)));
            await call.RequestStream.WriteAsync(new TelemetryIqBlock
            {
                FirstSampleIndex = _index, SampleRate = IqChannelizer.OutputRate, Cf32 = bytes
            }, ct);
            _index += (ulong)(count / 2);
        }
    }

    internal async Task EndAsync()
    {
        if (_call is null) return;
        using var call = _call;
        _call = null;
        await call.RequestStream.CompleteAsync();
        var receipt = await call.ResponseAsync;
        if ((ulong)receipt.Samples != _index || receipt.SourceEpoch == 0)
            throw new IOException("DSP IQ receipt did not match sent samples.");
        Console.WriteLine($"DSP epoch {receipt.SourceEpoch}: {receipt.Samples} samples");
        _index = 0;
    }

    public void Dispose() { _call?.Dispose(); _channel.Dispose(); }
}
