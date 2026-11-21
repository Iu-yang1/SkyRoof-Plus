using System.Security.Cryptography;
using System.Text.Json;

namespace SkyRoof.Remote.Foundation.Timing;

public enum TimedWaveformFormat { Pcm16Le = 2, Float32Le = 4 }

public sealed record TimedWaveformArtifact(
    Guid JobId, int SampleRate, int Channels, TimedWaveformFormat Format,
    long SampleFrames, long PayloadBytes, string Sha256, string FileName);

public sealed partial class TimedWaveformStore(string rootDirectory)
{
    private readonly string _root = Path.GetFullPath(rootDirectory);

    public async Task<TimedWaveformArtifact> ReadSealedAsync(Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(_root, jobId.ToString("N") + ".json");
        await using var input = File.OpenRead(file);
        var artifact = await JsonSerializer.DeserializeAsync<TimedWaveformArtifact>(
            input, cancellationToken: cancellationToken);
        return artifact ?? throw new InvalidDataException("Invalid sealed waveform manifest.");
    }

    public async Task<bool> VerifyAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var metadata = await ReadSealedAsync(jobId, cancellationToken);
        var path = Path.Combine(_root, metadata.FileName);
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var bytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return stream.Length == metadata.PayloadBytes &&
            Convert.ToHexString(bytes) == metadata.Sha256;
    }
}
