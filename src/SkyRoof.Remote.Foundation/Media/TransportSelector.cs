namespace SkyRoof.Remote.Foundation.Media;

public enum MediaCodec { Opus, Pcm16, Float32 }
public enum MediaTransport { AudioTrack, DataChannel, WebSocket }
public enum MediaFailure { IceUnavailable, TrackNegotiation, RtpStalled, DataChannelStalled, WebSocketCongested }

public sealed record MediaCapabilities(bool AudioTrack, bool DataChannel, bool WebSocket, bool IceReady);

public static class TransportSelector
{
    public static MediaTransport? ChooseInitial(MediaCodec codec, MediaCapabilities capabilities)
        => FirstAvailable(codec, capabilities, codec == MediaCodec.Opus
            ? [MediaTransport.AudioTrack, MediaTransport.DataChannel, MediaTransport.WebSocket]
            : [MediaTransport.DataChannel, MediaTransport.WebSocket]);

    public static MediaTransport? ChooseFallback(
        MediaCodec codec, MediaTransport current, MediaFailure failure, MediaCapabilities capabilities)
    {
        MediaTransport[] candidates = (current, failure) switch
        {
            (MediaTransport.AudioTrack, MediaFailure.IceUnavailable)
                => [MediaTransport.WebSocket, MediaTransport.DataChannel],
            (MediaTransport.AudioTrack, _)
                => [MediaTransport.DataChannel, MediaTransport.WebSocket],
            (MediaTransport.DataChannel, _)
                => [MediaTransport.WebSocket, MediaTransport.AudioTrack],
            (MediaTransport.WebSocket, _)
                => [MediaTransport.DataChannel, MediaTransport.AudioTrack],
            _ => [],
        };
        if (failure == MediaFailure.IceUnavailable)
            capabilities = capabilities with { IceReady = false };
        return FirstAvailable(codec, capabilities, candidates);
    }

    private static MediaTransport? FirstAvailable(
        MediaCodec codec, MediaCapabilities capabilities, IEnumerable<MediaTransport> candidates)
    {
        foreach (var transport in candidates)
        {
            if (transport == MediaTransport.AudioTrack
                && codec == MediaCodec.Opus && capabilities.AudioTrack && capabilities.IceReady)
                return transport;
            if (transport == MediaTransport.DataChannel
                && capabilities.DataChannel && capabilities.IceReady)
                return transport;
            if (transport == MediaTransport.WebSocket && capabilities.WebSocket)
                return transport;
        }
        return null;
    }
}
