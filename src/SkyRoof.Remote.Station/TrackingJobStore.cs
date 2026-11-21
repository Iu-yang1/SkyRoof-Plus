using Google.Protobuf;
using SkyRoof.Remote.Ipc;
using System.Text.Json;

namespace SkyRoof.Remote.Station;

public sealed record SavedTrackingJob(string Id, string RequestBase64);

public sealed class TrackingJobStore
{
    private readonly string _path = Path.GetFullPath(
        Environment.GetEnvironmentVariable("SKYROOF_TRACKING_JOB_FILE") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyRoof", "Remote", "tracking-" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                    Environment.GetEnvironmentVariable("SKYROOF_STATION_PIPE") ??
                    "skyroof-station-core")))[..16] + ".json"));

    public (string Id, TrackingJobStart Request)? Load()
    {
        if (!File.Exists(_path)) return null;
        var saved = JsonSerializer.Deserialize<SavedTrackingJob>(File.ReadAllBytes(_path))
            ?? throw new InvalidDataException("Invalid tracking job file.");
        return (saved.Id, TrackingJobStart.Parser.ParseFrom(Convert.FromBase64String(saved.RequestBase64)));
    }

    public void Save(string id, TrackingJobStart request)
    {
        var folder = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Tracking job directory is missing.");
        Directory.CreateDirectory(folder);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(
                new SavedTrackingJob(id, Convert.ToBase64String(request.ToByteArray())));
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Clear() => File.Delete(_path);
}
