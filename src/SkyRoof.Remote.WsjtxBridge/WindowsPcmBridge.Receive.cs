using System.Net.WebSockets;

namespace SkyRoof.Remote.WsjtxBridge;

internal sealed partial class WindowsPcmBridge
{
    private readonly RxPcmTimeline _timeline = new();

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
        var block = _timeline.Accept(packet);
        if (block is null) return;
        if (block.ResetBuffer || _rx.BufferedBytes + block.Pcm.Length > _rx.BufferLength)
            _rx.ClearBuffer();
        _rx.AddSamples(block.Pcm, 0, block.Pcm.Length);
    }
}
