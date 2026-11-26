using System.Globalization;
using System.Text.Json;

namespace SkyRoof.Remote.Foundation.Signals;

public sealed record TelemetryProductRecord(Guid Id, string Format, string Label, int Received,
    int Expected, bool Complete, bool Final, int Revision, DateTimeOffset Utc, DateTimeOffset UpdatedUtc,
    int Norad, string Satellite, string Transmitter, long Bytes);

public static class TelemetryProductStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static bool ValidFormat(string format) => format is "jpg" or "png" or "wav" or "txt";

    public static string ProductPath(string root, Guid id, string format) =>
        Path.Combine(root, "_products", id.ToString("N") + "." + format);

    public static string CatalogPath(string root, int norad, string day) =>
        Path.Combine(root, "_products", "catalog", norad.ToString(CultureInfo.InvariantCulture), day);

    public static string Cursor(TelemetryProductRecord record) =>
        record.Utc.UtcTicks.ToString("D19", CultureInfo.InvariantCulture) + "-" + record.Id.ToString("N");

    public static void Save(string root, TelemetryProductRecord record, byte[] bytes)
    {
        if (!ValidFormat(record.Format) || record.Id == Guid.Empty || record.Bytes != bytes.Length)
            throw new InvalidDataException("Invalid media product.");
        AtomicWrite(ProductPath(root, record.Id, record.Format), bytes);
        if (record.Norad <= 0) return;
        string day = record.Utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string metadata = Path.Combine(CatalogPath(root, record.Norad, day), Cursor(record) + ".json");
        AtomicWrite(metadata, JsonSerializer.SerializeToUtf8Bytes(record, Json));
    }

    public static TelemetryProductRecord? Read(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<TelemetryProductRecord>(input, Json);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string part = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            File.WriteAllBytes(part, bytes);
            File.Move(part, path, overwrite: true);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }
}
