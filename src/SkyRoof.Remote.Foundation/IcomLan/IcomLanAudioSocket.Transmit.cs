using SkyRoof.Remote.Foundation.Media;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanAudioSocket
{
    // Called only by the owner's paced TX worker. No PTT is asserted here.
    public async Task SendPcmAsync(ReadOnlyMemory<short> samples,
        IcomLanAudioCodec codec, CancellationToken ct)
    {
        if (_heartbeat is null || _lifetime.IsCancellationRequested)
            throw new IOException("Native audio session is not connected.");
        int maxSamples = codec == IcomLanAudioCodec.Lpcm16Mono ? 682 : 1364;
        await _sendLock.WaitAsync(ct);
        try
        {
            for (int offset = 0; offset < samples.Length; offset += maxSamples)
            {
                int count = Math.Min(maxSamples, samples.Length - offset);
                byte[] packet = IcomLanAudioPacketCodec.EncodeTransmit(
                    samples.Span.Slice(offset, count), LocalSid, RemoteSid,
                    unchecked(_txOuterSequence++), unchecked(_txAudioSequence++), codec);
                await _socket.SendAsync(packet, ct);
            }
        }
        finally { _sendLock.Release(); }
    }
}
