using System.Text;
using System.Text.Json;

namespace SkyRoof.Remote.Device;

public sealed record PttLeaseRecord(string BackendId, string TransactionId, string SessionId,
    DateTimeOffset StartedUtc);

public sealed class PttLeaseJournal(string path, string backendId)
{
    private readonly string _path = Path.GetFullPath(path);

    public bool Exists => File.Exists(_path);

    public PttLeaseRecord? Read()
    {
        if (!File.Exists(_path)) return null;
        var result = JsonSerializer.Deserialize<PttLeaseRecord>(File.ReadAllText(_path, Encoding.UTF8))
            ?? throw new InvalidDataException("PTT lease journal is unreadable.");
        if (result.BackendId != backendId)
            throw new InvalidOperationException("PTT journal belongs to a different CAT endpoint.");
        return result;
    }

    public void Save(string transactionId, string sessionId)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("PTT journal path has no directory.");
        Directory.CreateDirectory(directory);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            var record = new PttLeaseRecord(backendId, transactionId, sessionId,
                DateTimeOffset.UtcNow);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
