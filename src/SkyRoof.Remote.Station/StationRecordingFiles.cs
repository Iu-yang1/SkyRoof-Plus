using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

internal static partial class StationRecordingFiles
{
    internal static string DirectoryPath => Path.GetFullPath(
        Environment.GetEnvironmentVariable("SKYROOF_RECORDING_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyRoof", "Remote", "recordings"));

    [GeneratedRegex(@"\Askyroof-[0-9]{8}-[0-9]{6}-[a-f0-9]{32}(?:-part[0-9]{3,})?\.wav\z", RegexOptions.CultureInvariant)]
    private static partial Regex RecordingName();

    private static string Resolve(string name)
    {
        if (!RecordingName().IsMatch(name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Expected a station-generated WAV name."));
        string path = Path.Combine(DirectoryPath, name);
        if (!File.Exists(path)) throw new RpcException(new Status(StatusCode.NotFound, "Recording not found."));
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Recording is not a regular file."));
        return path;
    }

    private static bool IsFinalized(FileStream stream)
    {
        Span<byte> header = stackalloc byte[12];
        if (stream.Length < 44) return false;
        stream.ReadExactly(header);
        stream.Position = 0;
        return header[..4].SequenceEqual("RIFF"u8) && header[8..].SequenceEqual("WAVE"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]) == stream.Length - 8;
    }

    private static StationRecordingFile Inspect(string path)
    {
        var info = new FileInfo(path);
        var row = new StationRecordingFile { Name = info.Name, SizeBytes = info.Length,
            ModifiedUtcMs = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() };
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            row.Detail = "Not a regular recording";
            return row;
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            row.Finalized = IsFinalized(stream);
            row.Detail = row.Finalized ? "Ready" : "Incomplete WAV header";
        }
        catch (IOException) { row.Detail = "Recording open or unavailable"; }
        return row;
    }

    internal static StationRecordingListResult List(StationRecordingListRequest request, CancellationToken token)
    {
        int limit = Math.Clamp(request.Limit == 0 ? 100 : request.Limit, 1, 200);
        if (request.BeforeName.Length > 0 && !RecordingName().IsMatch(request.BeforeName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid recording cursor."));
        var result = new StationRecordingListResult();
        if (!Directory.Exists(DirectoryPath)) return result;
        var paths = Directory.EnumerateFiles(DirectoryPath, "skyroof-*.wav")
            .Where(path => RecordingName().IsMatch(Path.GetFileName(path)) &&
                (request.BeforeName.Length == 0 || string.CompareOrdinal(Path.GetFileName(path), request.BeforeName) < 0))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Take(limit + 1);
        foreach (string path in paths)
        {
            token.ThrowIfCancellationRequested();
            if (result.Files.Count == limit)
            {
                result.NextBeforeName = result.Files[^1].Name;
                break;
            }
            try { result.Files.Add(Inspect(path)); }
            catch (FileNotFoundException) { /* Concurrent deletion of a completed segment. */ }
        }
        return result;
    }

    internal static async Task DownloadAsync(string name, IServerStreamWriter<StationRecordingChunk> response, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(Resolve(name), FileMode.Open, FileAccess.Read, FileShare.Read,
                262144, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (!IsFinalized(stream)) throw new RpcException(new Status(StatusCode.FailedPrecondition, "WAV is not finalized."));
            long length = stream.Length;
            byte[] buffer = new byte[262144];
            while (stream.Position < length)
            {
                long offset = stream.Position;
                int read = await stream.ReadAsync(buffer.AsMemory(), token);
                if (read == 0) throw new EndOfStreamException("Recording was truncated.");
                await response.WriteAsync(new StationRecordingChunk { Offset = offset, TotalBytes = length,
                    Data = ByteString.CopyFrom(buffer, 0, read) }, token);
            }
        }
        catch (IOException error) { throw new RpcException(new Status(StatusCode.FailedPrecondition, error.Message)); }
    }

    internal static StationRecordingDeleteResult Delete(string name)
    {
        string path = Resolve(name);
        try
        {
            // Windows denies deletion while the writer or any download holds the file open.
            File.Delete(path);
            return new StationRecordingDeleteResult { Name = name };
        }
        catch (IOException error) { throw new RpcException(new Status(StatusCode.FailedPrecondition, error.Message)); }
    }
}

public sealed partial class StationRecorderService
{
    public override Task<StationRecordingListResult> ListFiles(StationRecordingListRequest request, ServerCallContext context) =>
        Task.FromResult(StationRecordingFiles.List(request, context.CancellationToken));

    public override Task Download(StationRecordingFileRequest request, IServerStreamWriter<StationRecordingChunk> response, ServerCallContext context) =>
        StationRecordingFiles.DownloadAsync(request.Name, response, context.CancellationToken);

    public override Task<StationRecordingDeleteResult> DeleteFile(StationRecordingFileRequest request, ServerCallContext context) =>
        Task.FromResult(StationRecordingFiles.Delete(request.Name));
}
