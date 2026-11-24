using System.Buffers.Binary;
using System.Net.WebSockets;

namespace SkyRoof.Remote.WsjtxBridge;

internal sealed partial class WindowsPcmBridge
{
    private uint _epoch;
    private ulong _next;

    private async Task ReceiveAsync(Uri station, string token, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        var address = new UriBuilder(new Uri(station, "ws/audio"))
        { Scheme = station.Scheme == "https" ? "wss" : "ws" }.Uri;
        await socket.ConnectAsync(address, ct);
        byte[] message = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            int length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(message.AsMemory(length), ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                length += result.Count;
                if (length == message.Length && !result.EndOfMessage)
                    throw new InvalidDataException("RX audio packet exceeds the SignalBus frame size.");
            } while (!result.EndOfMessage);
            if (result.MessageType == WebSocketMessageType.Binary) ReceivePcm(message.AsSpan(0, length));
        }
    }

    private void ReceivePcm(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 52 || BinaryPrimitives.ReadUInt32LittleEndian(packet) != 0x31504153 ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[40..]) != 48000)
            throw new InvalidDataException("Invalid SAP/1 RX PCM packet.");
        uint epoch = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
        ulong first = BinaryPrimitives.ReadUInt64LittleEndian(packet[24..]);
        int samples = BinaryPrimitives.ReadUInt16LittleEndian(packet[44..]);
        int channels = packet[46];
        if (channels is < 1 or > 2 || packet.Length != 52 + samples * channels * 2)
            throw new InvalidDataException("Invalid SAP/1 channel or payload size.");
        if (_epoch != epoch || _next != first || _rx.BufferedBytes + samples * 2 > _rx.BufferLength)
            _rx.ClearBuffer();
        _epoch = epoch;
        _next = first + (ulong)samples;
        byte[] pcm = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            int offset = 52 + i * channels * 2;
            int value = BinaryPrimitives.ReadInt16LittleEndian(packet[offset..]);
            if (channels == 2) value = (value + BinaryPrimitives.ReadInt16LittleEndian(packet[(offset + 2)..])) / 2;
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)value);
        }
        _rx.AddSamples(pcm, 0, pcm.Length);
    }
}
