using System.Text.Json;
using SkyRoof.CW;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Dsp;

internal static class CwHistoryReader
{
    private sealed record Identity(Guid Session, long Generation, CwDecoderMode Mode, bool Hint, int Id);

    public static CwHistoryReply Read(CancellationToken ct)
    {
        string folder = Environment.GetEnvironmentVariable("SKYROOF_CW_HISTORY_DIRECTORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SkyRoof", "Remote", "CW", "History");
        var reply = new CwHistoryReply();
        var entries = new Dictionary<Identity, CwHistoryEntry>();
        try
        {
            if (!Directory.Exists(folder)) return reply;
            foreach (string path in Directory.EnumerateFiles(folder, "cw-*.jsonl").OrderByDescending(x => x).Take(2).Reverse())
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var lines = new StreamReader(file);
                var recent = new Queue<string>();
                while (lines.ReadLine() is { } line)
                {
                    ct.ThrowIfCancellationRequested();
                    recent.Enqueue(line);
                    if (recent.Count > 4096) recent.Dequeue();
                }
                foreach (string line in recent)
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<CwArchiveEvent>(line) is { } item && item.Utc.Kind == DateTimeKind.Utc)
                            Append(entries, item);
                    }
                    catch (JsonException error) { reply.Error = "Some archive events could not be read: " + error.Message; }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { reply.Error = error.Message; }
        reply.Entries.AddRange(entries.Values.OrderByDescending(e => e.LastWindowEndUtc).Take(100));
        return reply;
    }

    private static void Append(Dictionary<Identity, CwHistoryEntry> entries, CwArchiveEvent item)
    {
        var key = new Identity(item.SessionId, item.TimelineGeneration, item.Mode, item.UsesAssociationHint, item.IdentityId);
        if (!entries.TryGetValue(key, out var entry))
        {
            entry = new CwHistoryEntry { SessionId = key.Session.ToString(), TimelineGeneration = key.Generation,
                Mode = key.Mode.ToString(), UsesAssociationHint = key.Hint, IdentityId = key.Id };
            entries.Add(key, entry);
        }
        string utc = item.Utc.ToString("O");
        if (string.CompareOrdinal(utc, entry.LastWindowEndUtc) > 0) entry.LastWindowEndUtc = utc;
        string combined = entry.CommittedText + item.Delta;
        entry.CommittedText = combined.Length <= 4096 ? combined : combined[^4096..];
    }
}
