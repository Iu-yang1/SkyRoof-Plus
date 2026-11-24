using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SkyRoof.Remote.WsjtxBridge;

public sealed class RigctlListener(IRemoteRig rig, DigitalPttSession ptt, int port)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Console.WriteLine("Hamlib NET rigctl listening on " + listener.LocalEndpoint);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                try { await ServeAsync(client, ct); }
                catch (Exception e) when (e is IOException or SocketException)
                { Console.Error.WriteLine("CAT connection ended: " + e.Message); }
                finally
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await ptt.SetAsync(false, 0, "", timeout.Token);
                }
            }
        }
        finally { listener.Stop(); }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };
        var session = new RigctlSession(rig, ptt);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null || line.Trim() == "q") return;
            string answer = await session.ExecuteAsync(line, ct);
            await writer.WriteLineAsync(answer.AsMemory(), ct);
        }
    }
}
