using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SkyRoof.Remote.Foundation.IcomLan;

internal static class IcomLanCivQueryTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        using var radio = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        int port = (radio.Client.LocalEndPoint as IPEndPoint ??
            throw new InvalidOperationException("Fake CI-V socket was not bound.")).Port;
        var simulator = ServeAsync(radio, deadline.Token);
        await using var civ = IcomLanCivSocket.Reserve("127.0.0.1", port);
        await civ.ConnectAsync("127.0.0.1", port, deadline.Token);
        var frequency = new IcomCivQuery(0xA2, 0xE0, [0x03], 5);
        var answer = await civ.QueryAsync(frequency, deadline.Token);
        if (!answer.SequenceEqual(new byte[] { 0, 0, 0x65, 0x35, 0x04 }))
            throw new InvalidOperationException("Incorrect CI-V frequency response.");
        var ptt = new IcomCivQuery(0xA2, 0xE0, [0x1C, 0], 1);
        var state = await civ.QueryAsync(ptt, deadline.Token);
        if (state.Length != 1 || state[0] != 0)
            throw new InvalidOperationException("Incorrect CI-V PTT response.");
        await simulator;
        await CheckTimeoutAsync(civ, frequency, deadline.Token);
        Console.WriteLine("PASS: real UDP CI-V read queries, SID, command/subcommand and replay filtering.");
    }

    private static async Task CheckTimeoutAsync(IcomLanCivSocket civ,
        IcomCivQuery query, CancellationToken ct)
    {
        try
        {
            _ = await civ.QueryAsync(query, ct);
            throw new InvalidOperationException("Missing CI-V reply was accepted.");
        }
        catch (TimeoutException) { Console.WriteLine("Expected CI-V query timeout."); }
        if (!civ.QueryFailureTask.IsCompleted)
            throw new InvalidOperationException("Timed-out CI-V session was not invalidated.");
        try
        {
            _ = await civ.QueryAsync(query, ct);
            throw new InvalidOperationException("Invalidated CI-V session was reused.");
        }
        catch (IOException)
        {
            Console.WriteLine("PASS: timeout invalidates the CI-V session; no ambiguous retry.");
        }
    }

    private static async Task ServeAsync(UdpClient radio, CancellationToken ct)
    {
        const uint sid = 0xAABBCCDD;
        var greeting = await radio.ReceiveAsync(ct);
        var peer = greeting.RemoteEndPoint;
        uint local = BinaryPrimitives.ReadUInt32BigEndian(greeting.Buffer.AsSpan(8));
        _ = await radio.ReceiveAsync(ct);
        await radio.SendAsync(IcomLanControlCodec.ControlPacket(4, 0, sid, local), peer, ct);
        _ = await radio.ReceiveAsync(ct);
        _ = await radio.ReceiveAsync(ct);
        await radio.SendAsync(IcomLanControlCodec.ControlPacket(6, 1, sid, local), peer, ct);
        _ = await radio.ReceiveAsync(ct);
        await ExpectQueryAsync(radio, sid, local, [0x03], ct);
        await SendReplyAsync(radio, peer, new ReplyAddress(sid, local + 1), 1,
            [0xFE, 0xFE, 0xE0, 0xA2, 0x03, 9, 9, 9, 9, 9, 0xFD], ct);
        await SendReplyAsync(radio, peer, new ReplyAddress(sid, local), 2,
            [0xFE, 0xFE, 0xE0, 0xA2, 0x03, 0, 0, 0x65, 0x35, 0x04, 0xFD], ct);
        await ExpectQueryAsync(radio, sid, local, [0x1C, 0], ct);
        await SendReplyAsync(radio, peer, new ReplyAddress(sid, local), 2,
            [0xFE, 0xFE, 0xE0, 0xA2, 0x1C, 0, 1, 0xFD], ct);
        await SendReplyAsync(radio, peer, new ReplyAddress(sid, local), 3,
            [0xFE, 0xFE, 0xE0, 0xA2, 0x1C, 1, 1, 0xFD], ct);
        await SendReplyAsync(radio, peer, new ReplyAddress(sid, local), 4,
            [0xFE, 0xFE, 0xE0, 0xA2, 0x1C, 0, 0, 0xFD], ct);
    }

    private static async Task ExpectQueryAsync(UdpClient radio, uint sid,
        uint local, byte[] command, CancellationToken ct)
    {
        while (true)
        {
            byte[] packet = (await radio.ReceiveAsync(ct)).Buffer;
            if (!IcomLanCivPacketCodec.TryRead(packet, sid, local, out var payload)) continue;
            if (!payload.Span.SequenceEqual(new IcomCivQuery(0xA2, 0xE0, command, 0).BuildRequest()))
                throw new InvalidOperationException("Unexpected outbound CI-V command.");
            return;
        }
    }

    private readonly record struct ReplyAddress(uint Sender, uint Receiver);

    private static Task SendReplyAsync(UdpClient radio, IPEndPoint peer,
        ReplyAddress address, ushort sequence, byte[] frame, CancellationToken ct)
    {
        var packet = IcomLanCivPacketCodec.Encode(frame, address.Sender, address.Receiver, sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), sequence);
        return radio.SendAsync(packet, peer, ct).AsTask();
    }
}
