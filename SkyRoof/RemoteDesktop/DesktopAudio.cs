using System.Threading.Channels;
using NAudio.Wave;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed class DesktopRxPlayback : IDisposable
{
    private BufferedWaveProvider? buffer;
    private WaveOutEvent? output;
    private ulong epoch;
    private ulong nextSample;

    internal void Accept(StationSignalFrame frame)
    {
        if (frame.SampleRate != 48000 || frame.Channels is < 1 or > 2 ||
            frame.Payload.Length != frame.SampleCount * frame.Channels * 2)
            throw new InvalidDataException("Unexpected RX PCM geometry.");
        if (buffer is null || buffer.WaveFormat.Channels != frame.Channels)
            Reset(frame.Channels);
        var active = buffer ?? throw new InvalidOperationException("RX audio buffer did not initialize.");
        if (epoch != frame.Epoch || nextSample != frame.FirstSampleIndex)
            active.ClearBuffer();
        epoch = frame.Epoch;
        nextSample = frame.FirstSampleIndex + (ulong)frame.SampleCount;
        var bytes = frame.Payload.ToByteArray();
        if (active.BufferedBytes + bytes.Length > active.BufferLength)
            active.ClearBuffer();
        active.AddSamples(bytes, 0, bytes.Length);
    }

    private void Reset(int channels)
    {
        output?.Dispose();
        buffer = new BufferedWaveProvider(new WaveFormat(48000, 16, channels))
        {
            BufferDuration = TimeSpan.FromMilliseconds(250), DiscardOnBufferOverflow = true
        };
        output = new WaveOutEvent { DesiredLatency = 100 };
        output.Init(buffer);
        output.Play();
    }

    public void Dispose() => output?.Dispose();
}

internal sealed class DesktopMicrophone : IDisposable
{
    private readonly WaveInEvent input = new() { WaveFormat = new WaveFormat(48000, 16, 1), BufferMilliseconds = 20 };
    private readonly CancellationTokenSource lifetime;
    private readonly Channel<(ulong Sequence, byte[] Pcm)> queue = Channel.CreateBounded<(ulong, byte[])>(
        new BoundedChannelOptions(8) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly StationIpcClient client;
    private readonly string transaction;
    private readonly ulong epoch;
    private readonly byte[] pending = new byte[1920];
    private int count;
    private ulong sequence;
    private Task? sender;

    internal DesktopMicrophone(StationIpcClient client, string transaction, ulong epoch, CancellationToken token)
    {
        this.client = client;
        this.transaction = transaction;
        this.epoch = epoch;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        input.DataAvailable += Captured;
    }

    internal void Start()
    {
        input.StartRecording();
        sender = SendAsync(lifetime.Token);
    }

    private void Captured(object? sender, WaveInEventArgs e)
    {
        var remaining = e.Buffer.AsSpan(0, e.BytesRecorded);
        while (!remaining.IsEmpty)
        {
            int take = Math.Min(remaining.Length, pending.Length - count);
            remaining[..take].CopyTo(pending.AsSpan(count));
            count += take;
            remaining = remaining[take..];
            if (count != pending.Length) continue;
            queue.Writer.TryWrite((sequence++, pending.ToArray()));
            count = 0;
        }
    }

    private async Task SendAsync(CancellationToken token)
    {
        try
        {
            await foreach (var packet in queue.Reader.ReadAllAsync(token))
            {
                var receipt = await client.PushPcmAsync(transaction, epoch, packet.Sequence, packet.Pcm, token).ConfigureAwait(false);
                if (!receipt.Accepted && receipt.State != "duplicate") return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Grpc.Core.RpcException error) { Serilog.Log.Warning("Desktop mic RPC failed: {Detail}", error.Status.Detail); }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        queue.Writer.TryComplete();
        input.DataAvailable -= Captured;
        input.Dispose();
        // The sender owns no device after the transaction is released.
        _ = sender;
        lifetime.Dispose();
    }
}
