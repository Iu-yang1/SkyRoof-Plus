using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SkyRoof.Remote.Foundation.IcomLan;
using SkyRoof.Remote.Foundation.Media;

internal static class IcomLanTxAudioTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        using var radio = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        int port = (radio.Client.LocalEndPoint as IPEndPoint ??
            throw new InvalidOperationException("TX test radio not bound.")).Port;
        Task receiver = ReceiveAsync(radio, timeout.Token);
        await using var audio = await IcomLanAudioSocket.OpenAsync(
            "127.0.0.1", port, timeout.Token);
        short[] samples = Enumerable.Range(0, 960).Select(i => (short)i).ToArray();
        await audio.SendPcmAsync(samples, IcomLanAudioCodec.Lpcm16Mono, timeout.Token);
        await receiver;
        Console.WriteLine("PASS: 48-kHz TX 20-ms PCM split into sequential MTU-safe native UDP packets.");
    }

    private static async Task ReceiveAsync(UdpClient radio, CancellationToken ct)
    {
        const uint sid = 0xAABBCCDD;
        var hello = await radio.ReceiveAsync(ct);
        uint local = BinaryPrimitives.ReadUInt32BigEndian(hello.Buffer.AsSpan(8));
        var peer = hello.RemoteEndPoint;
        _ = await radio.ReceiveAsync(ct);
        await radio.SendAsync(IcomLanControlCodec.ControlPacket(4, 0, sid, local), peer, ct);
        _ = await radio.ReceiveAsync(ct);
        _ = await radio.ReceiveAsync(ct);
        await radio.SendAsync(IcomLanControlCodec.ControlPacket(6, 1, sid, local), peer, ct);
        var samples = new List<short>();
        ushort sequence = 0;
        while (samples.Count < 960)
        {
            byte[] packet = (await radio.ReceiveAsync(ct)).Buffer;
            if (!IcomLanAudioPacketCodec.TryDecode(packet, local, sid,
                IcomLanAudioCodec.Lpcm16Mono, out var frame)) continue;
            if (frame.OuterSequence != sequence || frame.AudioSequence != sequence ||
                frame.Pcm16.Length != (sequence == 0 ? 1364 : 556))
                throw new InvalidOperationException("TX packet sequence or MTU split mismatch.");
            sequence++;
            for (int i = 0; i < frame.Pcm16.Length; i += 2)
                samples.Add(BinaryPrimitives.ReadInt16LittleEndian(frame.Pcm16.AsSpan(i)));
        }
        if (!samples.SequenceEqual(Enumerable.Range(0, 960).Select(i => (short)i)))
            throw new InvalidOperationException("Native TX PCM changed in UDP encoding.");
    }
}
