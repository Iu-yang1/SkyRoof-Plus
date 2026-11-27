using System.Diagnostics;
using System.Text.Json;
using SkyRoof.Remote.Foundation.Signals;

namespace SkyRoof.Remote.Dsp;

public sealed partial class TelemetryLiveReceiver
{
    private sealed record ProductIdentity(Guid Id, DateTimeOffset Started, int Revision, long Published);
    private readonly Dictionary<string, ProductIdentity> _mediaProducts = [];

    private void MediaDecoded(TelemetryMediaProduct product)
    {
        var key = product.StreamKey is null ? null : $"{_generation}:{product.Extension}:{product.StreamKey}";
        var previous = key is null ? null : _mediaProducts.GetValueOrDefault(key);
        if (!product.Final && previous is not null && Stopwatch.GetElapsedTime(previous.Published) < TimeSpan.FromSeconds(1))
            return;
        var identity = previous is null
            ? new ProductIdentity(Guid.NewGuid(), DateTimeOffset.UtcNow, 1, Stopwatch.GetTimestamp())
            : previous with { Revision = previous.Revision + 1, Published = Stopwatch.GetTimestamp() };
        try
        {
            var record = new TelemetryProductRecord(identity.Id, product.Extension, product.Label,
                product.Received, product.Expected, product.Complete, product.Final, identity.Revision,
                identity.Started, DateTimeOffset.UtcNow, _config.NoradId, _config.Satellite,
                _config.Transmitter, product.Bytes.Length);
            TelemetryProductStore.Save(ArchiveDirectory(), record, product.Bytes);
            if (key is not null)
            {
                if (product.Final) _mediaProducts.Remove(key);
                else _mediaProducts[key] = identity;
            }
            Publish(new { state = "media", utc = record.UpdatedUtc, generation = _generation,
                config = _config, id = record.Id.ToString("N"), format = record.Format,
                record.Label, record.Received, record.Expected, record.Complete, record.Final, record.Revision,
                bytes = record.Bytes });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Publish(new { state = "media-error", error = "Could not save decoded product: " + error.GetType().Name });
        }
    }
}
