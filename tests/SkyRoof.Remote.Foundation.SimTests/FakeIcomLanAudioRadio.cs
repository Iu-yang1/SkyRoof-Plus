using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

internal sealed class FakeIcomLanAudioRadio : IDisposable
{
    public const uint RadioSid = 0xCAFEBABE;
    private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
    public int Port => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;

    public async Task RunAsync(CancellationToken ct)
    {
        var greeting = await _socket.ReceiveAsync(ct);
        uint localSid = BinaryPrimitives.ReadUInt32BigEndian(greeting.Buffer.AsSpan(8, 4));
        var peer = greeting.RemoteEndPoint;
        _ = await _socket.ReceiveAsync(ct);
        await _socket.SendAsync(
            SkyRoof.Remote.Foundation.IcomLan.IcomLanControlCodec
                .ControlPacket(4, 0, RadioSid, localSid), peer, ct);
        _ = await _socket.ReceiveAsync(ct);
        _ = await _socket.ReceiveAsync(ct);
        await _socket.SendAsync(
            SkyRoof.Remote.Foundation.IcomLan.IcomLanControlCodec
                .ControlPacket(6, 1, RadioSid, localSid), peer, ct);

        var wrongSidAudio = AudioFrame(localSid + 1);
        await _socket.SendAsync(wrongSidAudio, peer, ct);
        var ping = new byte[21];
        BinaryPrimitives.WriteUInt32LittleEndian(ping, 21);
        BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(4, 2), 7);
        BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(8, 4), RadioSid);
        BinaryPrimitives.WriteUInt32BigEndian(ping.AsSpan(12, 4), localSid);
        ping[17] = 0x47; ping[18] = 0x30; ping[19] = 0x83; ping[20] = 6;
        await _socket.SendAsync(ping, peer, ct);

        int localPings = 0;
        bool remotePingAcknowledged = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (!remotePingAcknowledged || localPings < 1)
        {
            var received = await _socket.ReceiveAsync(timeout.Token);
            byte[] packet = received.Buffer;
            if (packet.Length != 21 || packet[20] != 6) continue;
            if (packet[16] == 0) localPings++;
            if (packet[16] == 1 &&
                BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4)) == localSid &&
                BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(12, 4)) == RadioSid &&
                packet[17] == 0x47) remotePingAcknowledged = true;
        }
        await _socket.SendAsync(AudioFrame(localSid), peer, ct);
        await Task.Delay(30, ct);
        await _socket.SendAsync(
            SkyRoof.Remote.Foundation.IcomLan.IcomLanControlCodec
                .ControlPacket(5, 0, RadioSid, localSid), peer, ct);
    }

    private static byte[] AudioFrame(uint localSid)
    {
        var bytes = new byte[24 + 960];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), RadioSid);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12, 4), localSid);
        bytes[16] = 0x80;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(18, 2), 9);
        Array.Fill(bytes, (byte)0x5A, 24, 960);
        return bytes;
    }

    public void Dispose() => _socket.Dispose();
}
