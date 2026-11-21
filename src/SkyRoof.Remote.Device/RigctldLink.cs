using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace SkyRoof.Remote.Device;

/// <summary>
/// Exclusive CAT TCP owner for one rigctld/SkyCAT endpoint. One in-flight command
/// and one response at a time; a failed write is never automatically replayed.
/// </summary>
public sealed class RigctldLink(string host, int port) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private async Task<string> SendAsync(string command, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var ct = timeout.Token;
            if (_client is null)
            {
                var client = new TcpClient();
                try { await client.ConnectAsync(host, port, ct); }
                catch { client.Dispose(); throw; }
                var stream = client.GetStream();
                _client = client;
                _reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                _writer = new StreamWriter(stream, Encoding.ASCII, 4096, leaveOpen: true)
                    { NewLine = "\n", AutoFlush = true };
            }
            await _writer!.WriteLineAsync(command.AsMemory(), ct);
            var reply = await _reader!.ReadLineAsync(ct);
            if (reply is null) throw new IOException("Rigctld disconnected before reply.");
            return reply.Trim();
        }
        catch
        {
            Reset();
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(string command, CancellationToken token)
    {
        var reply = await SendAsync(command, token);
        if (reply != "RPRT 0")
            throw new IOException("CAT command rejected: " + command + " : " + reply);
    }

    public async Task<long> ReadLongAsync(string command, CancellationToken token)
    {
        var reply = await SendAsync(command, token);
        if (!long.TryParse(reply, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new IOException("Malformed CAT reply to " + command + ": " + reply);
        return value;
    }

    public async Task<string> ReadStringAsync(string command, CancellationToken token) =>
        await SendAsync(command, token);

    private void Reset()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { Reset(); }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
