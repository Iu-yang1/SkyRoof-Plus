namespace SkyRoof.Remote.Dsp;

public sealed partial class TelemetryLiveReceiver
{
    private void MediaDecoded(TelemetryMediaProduct product)
    {
        string id = Guid.NewGuid().ToString("N");
        try
        {
            string folder = Path.Combine(ArchiveDirectory(), "_products");
            Directory.CreateDirectory(folder);
            string final = Path.Combine(folder, id + "." + product.Extension);
            string temporary = final + ".part";
            try
            {
                File.WriteAllBytes(temporary, product.Bytes);
                File.Move(temporary, final);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Publish(new { state = "media", utc = DateTimeOffset.UtcNow, generation = _generation,
                config = _config, id, format = product.Extension, product.Label, product.Received,
                product.Expected, product.Complete, bytes = product.Bytes.Length });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Publish(new { state = "media-error", error = "Could not save decoded product: " + error.GetType().Name });
        }
    }
}
