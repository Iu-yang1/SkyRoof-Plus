using System.Net;
using System.Net.Sockets;
using Grpc.Core;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly NumericUpDown wsjtxLogPort = new() { Minimum = 1, Maximum = 65535, Value = 2237, Width = 85 };
    private CancellationTokenSource? wsjtxLogSource;

    private async Task StartWsjtxLogAsync()
    {
        if (wsjtxLogSource is not null) return;
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, (int)wsjtxLogPort.Value));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        wsjtxLogSource = source;
        qsoStatus.Text = $"WSJT-X Logged ADIF: listening on 127.0.0.1:{wsjtxLogPort.Value}";
        try
        {
            while (!source.IsCancellationRequested)
            {
                var datagram = await receiver.ReceiveAsync(source.Token);
                try
                {
                    var adif = StationIpcClient.ReadLoggedAdif(datagram.Buffer);
                    if (adif is null) continue;
                    var records = StationIpcClient.ParseAdif(adif, QsoField("MY CALL"));
                    var count = await client.ImportAdifAsync(records, _ => { }, source.Token);
                    qsoStatus.Text = $"WSJT-X: confirmed {count} QSO record(s).";
                }
                catch (Exception error) when (error is IOException or ArgumentException or FormatException or OverflowException or RpcException)
                {
                    qsoStatus.Text = "WSJT-X log not confirmed: " + error.Message;
                }
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        finally
        {
            wsjtxLogSource = null;
            if (!IsDisposed) qsoStatus.Text = "WSJT-X log listener stopped.";
        }
    }

    private Task StopWsjtxLogAsync()
    {
        wsjtxLogSource?.Cancel();
        return Task.CompletedTask;
    }
}
