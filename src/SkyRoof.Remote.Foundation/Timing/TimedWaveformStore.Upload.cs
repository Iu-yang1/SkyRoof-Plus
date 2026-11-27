using System.Security.Cryptography;
using System.Text.Json;

namespace SkyRoof.Remote.Foundation.Timing;

public sealed partial class TimedWaveformStore
{
    public async Task<TimedWaveformArtifact> UploadAndSealAsync(
        WaveformUploadRequest request, Stream pcm,
        CancellationToken cancellationToken = default)
    {
        if (request.JobId == Guid.Empty || request.SampleRate <= 0 || request.Channels <= 0 || request.Frames <= 0 ||
            request.Format is not (TimedWaveformFormat.Pcm16Le or TimedWaveformFormat.Float32Le))
            throw new ArgumentException("Invalid waveform metadata.");
        ArgumentNullException.ThrowIfNull(pcm);
        var expectedBytes = checked(request.Frames * request.Channels * (int)request.Format);
        Directory.CreateDirectory(_root);
        var fileName = request.JobId.ToString("N") + ".pcm";
        var destination = Path.Combine(_root, fileName);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await pcm.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length = checked(length + read);
                    if (length > expectedBytes)
                        throw new InvalidDataException("More PCM samples than declared.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (length != expectedBytes)
                    throw new InvalidDataException("PCM payload shorter than declared.");
                await output.FlushAsync(cancellationToken);
            }
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            var result = new TimedWaveformArtifact(request.JobId, request.SampleRate, request.Channels, request.Format,
                request.Frames, length, digest, fileName);
            var manifest = Path.Combine(_root, request.JobId.ToString("N") + ".json");
            if (File.Exists(manifest))
            {
                var existing = await ReadSealedAsync(request.JobId, cancellationToken);
                if (existing != result || !await VerifyAsync(request.JobId, cancellationToken))
                    throw new InvalidOperationException("Job ID already sealed with different content.");
                return existing;
            }
            if (File.Exists(destination))
                throw new InvalidOperationException("Waveform exists without committed manifest.");
            File.Move(temporary, destination);
            await using (var manifestFile = new FileStream(manifest, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(manifestFile, result,
                    cancellationToken: cancellationToken);
                await manifestFile.FlushAsync(cancellationToken);
            }
            return result;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
