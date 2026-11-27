using System.Threading.Channels;
using NAudio.Wave;

namespace SkyRoof.Remote.WsjtxBridge;

internal sealed partial class WindowsPcmBridge : IAsyncDisposable
{
    private readonly IRemoteRig _rig;
    private readonly DigitalPttSession _ptt;
    private readonly WaveInEvent _capture;
    private readonly WaveOutEvent _playback;
    private readonly BufferedWaveProvider _rx;
    private readonly Channel<TxFrame> _queue = Channel.CreateBounded<TxFrame>(new BoundedChannelOptions(12)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly object _captureGate = new();
    private readonly byte[] _pending = new byte[1920];
    private int _filled;
    private ulong _sequence;
    private TxGrant? _captureGrant;
    private sealed record TxFrame(TxGrant Grant, ulong Sequence, byte[] Pcm);

    public WindowsPcmBridge(IRemoteRig rig, DigitalPttSession ptt, int outputIndex, int inputIndex)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows virtual audio devices required.");
        _rig = rig;
        _ptt = ptt;
        var format = new WaveFormat(48000, 16, 1);
        _rx = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(300), ReadFully = true };
        _capture = new WaveInEvent { DeviceNumber = inputIndex, WaveFormat = format, BufferMilliseconds = 20, NumberOfBuffers = 3 };
        _playback = new WaveOutEvent { DeviceNumber = outputIndex, DesiredLatency = 100, NumberOfBuffers = 3 };
        _playback.Init(_rx);
        _ptt.GrantChanged += SelectGrant;
        _capture.DataAvailable += Captured;
    }

    public static void ListDevices()
    {
        for (int i = 0; i < WaveOut.DeviceCount; i++) Console.WriteLine($"RX output {i}: {WaveOut.GetCapabilities(i).ProductName}");
        for (int i = 0; i < WaveInEvent.DeviceCount; i++) Console.WriteLine($"TX input {i}: {WaveInEvent.GetCapabilities(i).ProductName}");
    }

    private void SelectGrant(TxGrant? grant)
    {
        lock (_captureGate)
        {
            _captureGrant = grant;
            _filled = 0;
            _sequence = 0;
        }
    }

    private void Captured(object? sender, WaveInEventArgs e)
    {
        TxGrant? failed = null;
        lock (_captureGate)
        {
            if (_captureGrant is not { } grant) return;
            int offset = 0;
            while (offset < e.BytesRecorded)
            {
                int count = Math.Min(e.BytesRecorded - offset, _pending.Length - _filled);
                Buffer.BlockCopy(e.Buffer, offset, _pending, _filled, count);
                offset += count;
                _filled += count;
                if (_filled != _pending.Length) continue;
                if (!_queue.Writer.TryWrite(new TxFrame(grant, _sequence++, (byte[])_pending.Clone())))
                {
                    _captureGrant = null;
                    failed = grant;
                    break;
                }
                _filled = 0;
            }
        }
        if (failed is not null) _ = AbortCaptureAsync(failed);
    }

    private async Task AbortCaptureAsync(TxGrant grant)
    {
        try
        {
            Console.Error.WriteLine("TX audio queue overrun; releasing this digital transaction.");
            await _ptt.AbortAsync(grant);
        }
        catch (Exception ex) { Console.Error.WriteLine("TX release failed: " + ex.Message); }
    }

    private async Task SendAsync(CancellationToken ct)
    {
        await foreach (var frame in _queue.Reader.ReadAllAsync(ct))
        {
            if (_ptt.Grant != frame.Grant) continue;
            var receipt = await _rig.SendPcmAsync(frame.Grant, frame.Sequence, frame.Pcm, ct);
            if (!receipt.Accepted && receipt.State != "duplicate") await _ptt.AbortAsync(frame.Grant);
        }
    }

    public ValueTask DisposeAsync()
    {
        _ptt.GrantChanged -= SelectGrant;
        _capture.DataAvailable -= Captured;
        _queue.Writer.TryComplete();
        _capture.Dispose();
        _playback.Dispose();
        return ValueTask.CompletedTask;
    }
}
