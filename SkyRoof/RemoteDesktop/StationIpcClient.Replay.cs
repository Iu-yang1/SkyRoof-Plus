using Google.Protobuf;
using System.Runtime.InteropServices;
using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal async Task<TelemetryIqReceipt> ReplayIqAsync(Stream source, CancellationToken token, int inputRate = 48000, double offsetHz = 0)
    {
        var channelizer = inputRate == 48000 && offsetHz == 0 ? null : new IqChannelizer(inputRate, offsetHz);
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
            var payload = channelizer is null ? ByteString.CopyFrom(data, 0, count) :
                ByteString.CopyFrom(MemoryMarshal.AsBytes(channelizer.Process(MemoryMarshal.Cast<byte, float>(data.AsSpan(0, count))).AsSpan()));
            if (payload.IsEmpty) continue;
            await call.RequestStream.WriteAsync(new TelemetryIqBlock
            {
                SampleRate = IqChannelizer.OutputRate, FirstSampleIndex = index, Cf32 = payload
            }, token);
            index += (ulong)(payload.Length / 8);
        }
        await call.RequestStream.CompleteAsync();
        var receipt = await call.ResponseAsync;
        if ((ulong)receipt.Samples != index) throw new IOException("IQ replay receipt mismatch.");
        return receipt;
    }
}
