using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

public sealed class FakeRigctld : IAsyncDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _task;
    private readonly object _sync = new();
    private readonly List<string> _commands = [];
    private long _rx = 145_900_000;
    private long _tx = 435_650_000;
    private bool _ptt;
    private string _rxMode = "USB", _txMode = "USB";
    private string? _rejectNext;

    public int Port { get; }

    public FakeRigctld()
    {
        _server.Start();
        Port = ((IPEndPoint)_server.LocalEndpoint).Port;
        _task = RunAsync();
    }

    public int Count(string command)
    {
        lock (_sync) return _commands.Count(x => x == command);
    }

    public void SetExternalPtt(bool value)
    {
        lock (_sync) _ptt = value;
    }

    public void RejectNext(string command)
    {
        lock (_sync) _rejectNext = command;
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _server.AcceptTcpClientAsync(_stop.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };
                while (!_stop.IsCancellationRequested)
                {
                    var cmd = await reader.ReadLineAsync(_stop.Token);
                    if (cmd is null) break;
                    string reply;
                    lock (_sync)
                    {
                        _commands.Add(cmd);
                        if (_rejectNext == cmd)
                        {
                            _rejectNext = null;
                            reply = "RPRT -1";
                        }
                        else
                            reply = Execute(cmd);
                    }
                    await writer.WriteLineAsync(reply.AsMemory(), _stop.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private string Execute(string command)
    {
        if (command == "f") return _rx.ToString(CultureInfo.InvariantCulture);
        if (command == "i") return _tx.ToString(CultureInfo.InvariantCulture);
        if (command == "t") return _ptt ? "1" : "0";
        if (command.StartsWith("F ", StringComparison.Ordinal))
            _rx = long.Parse(command.AsSpan(2), CultureInfo.InvariantCulture);
        else if (command.StartsWith("I ", StringComparison.Ordinal))
            _tx = long.Parse(command.AsSpan(2), CultureInfo.InvariantCulture);
        else if (command.StartsWith("M ", StringComparison.Ordinal))
            _rxMode = command[2..].Split(' ')[0];
        else if (command.StartsWith("X ", StringComparison.Ordinal))
            _txMode = command[2..].Split(' ')[0];
        else if (command == "T 1") _ptt = true;
        else if (command == "T 0") _ptt = false;
        else return "RPRT -1";
        return "RPRT 0";
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _server.Stop();
        await _task;
        _stop.Dispose();
    }
}
