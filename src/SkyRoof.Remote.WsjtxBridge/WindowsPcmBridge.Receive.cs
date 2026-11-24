using System.Buffers.Binary;
using System.Net.WebSockets;
using SkyRoof.Remote.Foundation.Signals;

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
        if (!AudioFrameHeader.TryRead(packet, out var header) || header.Codec != 2 || header.SampleRate != 48000)
            throw new InvalidDataException("Invalid SAP/1 RX PCM packet.");
        uint epoch = header.StreamEpoch;
        ulong first = header.FirstSampleIndex;
        int samples = header.SamplesPerChannel;
        int channels = header.Channels;
        if (channels is < 1 or > 2 || packet.Length != AudioFrameHeader.Size + samples * channels * 2 ||
            header.PayloadLength != samples * channels * 2)
            throw new InvalidDataException("Invalid SAP/1 channel or payload size.");
        if (_epoch != epoch || _next != first || _rx.BufferedBytes + samples * 2 > _rx.BufferLength)
            _rx.ClearBuffer();
        _epoch = epoch;
        _next = first + (ulong)samples;
        byte[] pcm = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            int offset = AudioFrameHeader.Size + i * channels * 2;
            int value = BinaryPrimitives.ReadInt16LittleEndian(packet[offset..]);
            if (channels == 2) value = (value + BinaryPrimitives.ReadInt16LittleEndian(packet[(offset + 2)..])) / 2;
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)value);
        }
        _rx.AddSamples(pcm, 0, pcm.Length);
    }
}
