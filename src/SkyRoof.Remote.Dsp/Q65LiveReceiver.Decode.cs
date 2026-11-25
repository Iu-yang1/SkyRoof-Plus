namespace SkyRoof.Remote.Dsp;

public sealed partial class Q65LiveReceiver
{
    private async Task DecodeAsync()
    {
        Q65Decoder? decoder = null;
        long generation = -1, previous = -1;
        try
        {
            await foreach (var work in _pending.Reader.ReadAllAsync())
            {
                lock (_gate) if (work.Generation != _generation) continue;
                try
                {
                    if (decoder is null || generation != work.Generation || work.Slot.Index != previous + 1)
                    {
                        decoder?.Dispose();
                        decoder = null;
                        decoder = new Q65Decoder(work.Geometry, work.Config);
                        generation = work.Generation;
                    }
                    previous = work.Slot.Index;
                    var messages = decoder.Decode(work.Slot.Samples, work.Slot.Index, 48000);
                    lock (_gate)
                        if (work.Generation == _generation)
                            Publish(new { state = "decoded", config = work.Config, messages,
                                utc = DateTimeOffset.FromUnixTimeSeconds(work.Slot.Index * work.Geometry.Seconds),
                                sourceEpoch = work.Slot.Epoch, timing = "estimated-publish-anchor" });
                }
                catch (Exception error) when (error is InvalidDataException or ArgumentException or DllNotFoundException or EntryPointNotFoundException)
                {
                    decoder?.Dispose();
                    decoder = null;
                    lock (_gate)
                        if (work.Generation == _generation) Publish(new { state = "error", config = work.Config, error = error.Message });
                }
            }
        }
        finally { decoder?.Dispose(); }
    }
}
