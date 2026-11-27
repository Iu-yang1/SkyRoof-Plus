using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SkyRoof.Remote.Foundation.IcomLan;

internal static class IcomLanCivWriteTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        using var radio = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        int port = (radio.Client.LocalEndPoint as IPEndPoint ??
            throw new InvalidOperationException("Fake CI-V socket is not bound.")).Port;
        var simulator = new IcomLanCivWriteRadio(radio);
        Task serve = simulator.RunAsync(deadline.Token);
        await using var socket = IcomLanCivSocket.Reserve("127.0.0.1", port);
        await socket.ConnectAsync("127.0.0.1", port, deadline.Token);
        var cat = new IcomLanCivControl(socket, 0xA2);
        await cat.SelectBandAsync(false, deadline.Token);
        await cat.SetSelectedFrequencyAsync(435_650_000, deadline.Token);
        await cat.SetSelectedModeAsync(new(1, 2), deadline.Token);
        await cat.SetPttAsync(true, deadline.Token);
        await cat.SetPttAsync(false, deadline.Token);
        var meters = new IcomLanCivMeters(socket, 0xA2);
        if (await meters.ReadAsync(IcomCivMeter.Signal, deadline.Token) != 120)
            throw new InvalidOperationException("S-meter BCD decoding failed.");
        try
        {
            await cat.SelectBandAsync(true, deadline.Token);
            throw new InvalidOperationException("FA was incorrectly accepted.");
        }
        catch (IOException error) when (error.Message.Contains("(FA)", StringComparison.Ordinal))
        {
            Console.WriteLine("PASS: native CI-V NAK propagated.");
        }
        await serve;
        Console.WriteLine("PASS: CI-V native setters, ACK plus hardware readbacks, RX/TX and meter BCD.");
    }
}
