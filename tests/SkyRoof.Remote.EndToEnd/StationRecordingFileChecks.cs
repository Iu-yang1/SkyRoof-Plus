using Grpc.Core;
using SkyRoof.Remote.Ipc;
using SkyRoof.RemoteDesktop;

internal static class StationRecordingFileChecks
{
    internal static async Task RunAsync(StationIpcClient client, StationRecordingStatus stopped, string directory, CancellationToken token)
    {
        var page = await client.ListStationRecordingsAsync("", token);
        foreach (var name in stopped.Files)
        {
            var entry = page.Files.SingleOrDefault(file => file.Name == name);
            if (entry is null || !entry.Finalized) throw new InvalidDataException("Finalized recording not listed.");
            using var downloaded = new MemoryStream();
            long length = await client.CopyStationRecordingAsync(name, downloaded, null, token);
            byte[] actual = File.ReadAllBytes(Path.Combine(directory, name));
            if (length != actual.Length || !downloaded.ToArray().AsSpan().SequenceEqual(actual))
                throw new InvalidDataException("Station WAV download changed bytes.");
        }
        try
        {
            using var sink = new MemoryStream();
            await client.CopyStationRecordingAsync("../tracking.json", sink, null, token);
            throw new InvalidDataException("Path traversal was accepted.");
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.InvalidArgument) { Console.WriteLine("PASS: invalid recording name rejected."); }
        string removed = stopped.Files[0];
        await client.DeleteStationRecordingAsync(removed, token);
        var after = await client.ListStationRecordingsAsync("", token);
        if (File.Exists(Path.Combine(directory, removed)) || after.Files.Any(file => file.Name == removed))
            throw new InvalidDataException("Station recording deletion did not persist.");
        Console.WriteLine("PASS: finalized Station WAV inventory, exact streaming download, path validation and deletion.");
    }
}
