using System.Text.Json;
using SkyRoof.Remote.Ipc;

internal static class CwHistoryProbe
{
    public static async Task RunAsync(DspWaveform.DspWaveformClient control, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        DateTime utc = DateTime.UtcNow;
        string path = Path.Combine(folder, $"cw-{utc:yyyy-MM-dd}.jsonl");
        var events = new[] {
            new { Utc = utc, TimelineGeneration = 1L, UsesAssociationHint = true, IdentityId = 3, Delta = "CQ ", SessionId = a, Mode = 0 },
            new { Utc = utc.AddSeconds(1), TimelineGeneration = 1L, UsesAssociationHint = true, IdentityId = 3, Delta = "K1ABC", SessionId = a, Mode = 0 },
            new { Utc = utc.AddSeconds(2), TimelineGeneration = 1L, UsesAssociationHint = true, IdentityId = 3, Delta = "W1XYZ", SessionId = b, Mode = 0 }
        };
        await File.WriteAllLinesAsync(path, events.Select(x => JsonSerializer.Serialize(x)), ct);
        var reply = await control.GetCwHistoryAsync(new CwHistoryRequest(), cancellationToken: ct);
        if (reply.Error.Length != 0 || reply.Entries.Count != 2 ||
            !reply.Entries.Any(x => x.SessionId == a.ToString() && x.CommittedText == "CQ K1ABC") ||
            !reply.Entries.Any(x => x.SessionId == b.ToString() && x.CommittedText == "W1XYZ"))
            throw new InvalidDataException("CW archive identities or deltas were merged incorrectly.");
        Console.WriteLine("PASS: independent DSP CW history IPC restores persisted deltas without merging distinct sessions.");
    }
}
