using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcClient
{
    internal Task<StationRecordingListResult> ListStationRecordingsAsync(string cursor, CancellationToken token) =>
        Recorder.ListFilesAsync(new StationRecordingListRequest { BeforeName = cursor, Limit = 100 },
            deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;

    internal Task<StationRecordingDeleteResult> DeleteStationRecordingAsync(string name, CancellationToken token) =>
        Recorder.DeleteFileAsync(new StationRecordingFileRequest { Name = name },
            deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token).ResponseAsync;

    internal async Task<long> CopyStationRecordingAsync(string name, Stream destination, IProgress<long>? progress, CancellationToken token)
    {
        using var call = Recorder.Download(new StationRecordingFileRequest { Name = name }, cancellationToken: token);
        long offset = 0, length = -1;
        while (await call.ResponseStream.MoveNext(token))
        {
            var chunk = call.ResponseStream.Current;
            if (length < 0) length = chunk.TotalBytes;
            if (chunk.Offset != offset || chunk.TotalBytes != length || chunk.Data.Length == 0 ||
                length < 44 || chunk.Data.Length > length - offset)
                throw new InvalidDataException("Incomplete or inconsistent Station recording stream.");
            await destination.WriteAsync(chunk.Data.Memory, token);
            offset += chunk.Data.Length;
            progress?.Report(offset);
        }
        if (offset != length || length < 44) throw new EndOfStreamException("Station recording download was incomplete.");
        return offset;
    }
}
