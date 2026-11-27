using System.Text.Json;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

internal sealed record StationRecordingIntent(string Id, StationSignalKind Kind)
{
    private static string FilePath => Path.Combine(StationRecordingFiles.DirectoryPath, "recording-job.json");

    internal static StationRecordingIntent? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var intent = JsonSerializer.Deserialize<StationRecordingIntent>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("Empty recording intent.");
        if (!Guid.TryParseExact(intent.Id, "N", out _) || !StationRecordingWorker.IsRecordingKind(intent.Kind))
            throw new InvalidDataException("Invalid recording intent.");
        return intent;
    }

    internal void Save()
    {
        string path = FilePath;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, this);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void Clear() => File.Delete(FilePath);
}
