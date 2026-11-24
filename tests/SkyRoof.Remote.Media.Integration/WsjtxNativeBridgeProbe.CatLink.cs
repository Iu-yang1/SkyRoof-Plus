using System.Net;
using System.Net.Sockets;

internal static partial class WsjtxNativeBridgeProbe
{
    private sealed class CatLink : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private CatLink(TcpClient client)
        {
            _client = client;
            _reader = new StreamReader(client.GetStream(), leaveOpen: true);
            _writer = new StreamWriter(client.GetStream(), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        }

        internal static async Task<CatLink> ConnectAsync(int port, CancellationToken ct)
        {
            var tcp = new TcpClient();
            try { await tcp.ConnectAsync(IPAddress.Loopback, port, ct); return new CatLink(tcp); }
            catch { tcp.Dispose(); throw; }
        }

        internal async Task ExpectAsync(string command, string expected, CancellationToken ct)
        {
            await _writer.WriteLineAsync(command.AsMemory(), ct);
            string? actual = await _reader.ReadLineAsync(ct);
            if (actual != expected) throw new InvalidDataException($"{command}: expected {expected}, got {actual}");
        }

        internal async Task VerifyHandshakeAsync(CancellationToken ct)
        {
            await _writer.WriteLineAsync("\\dump_state".AsMemory(), ct);
            if (await _reader.ReadLineAsync(ct) != "1")
                throw new InvalidDataException("Hamlib NET rigctl protocol handshake invalid.");
            for (int i = 0; i < 64; i++)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == "done") return;
                if (line is null) break;
            }
            throw new InvalidDataException("Hamlib NET rigctl protocol handshake incomplete.");
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.DisposeAsync();
            _reader.Dispose();
            _client.Dispose();
        }
    }
}
