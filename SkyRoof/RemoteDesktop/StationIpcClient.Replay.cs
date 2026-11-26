using Google.Protobuf;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal async Task<TelemetryIqReceipt> ReplayIqAsync(Stream source, CancellationToken token)
    {
        using var call = dsp.PublishTelemetryIq(cancellationToken: token);
        var data = new byte[32768];
        ulong index = 0;
        while (true)
        {
            int count = 0;
            while (count < data.Length)
            {
                int read = await source.ReadAsync(data.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            if (count == 0) break;
            if (count % 8 != 0) throw new InvalidDataException("Truncated CF32 sample.");
            await call.RequestStream.WriteAsync(new TelemetryIqBlock
            {
                SampleRate = 48000, FirstSampleIndex = index, Cf32 = ByteString.CopyFrom(data, 0, count)
            }, token);
            index += (ulong)(count / 8);
        }
        await call.RequestStream.CompleteAsync();
        var receipt = await call.ResponseAsync;
        if ((ulong)receipt.Samples != index) throw new IOException("IQ replay receipt mismatch.");
        return receipt;
    }
}
