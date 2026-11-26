using System.Buffers.Binary;
using NAudio.Wave;
using SkyRoof.AFC;

namespace SkyRoof.Remote.Station;

// Explicit real 12 kHz IF only. No demodulated AF or RF scope conversion.
internal sealed class StationIfSamples
{
    private readonly object gate = new();
    private readonly float[] ring = new float[48000];
    private int write, used;
    private long generation, samples, consumed;
    private DateTime lastUtc;
    internal long Samples { get { lock (gate) return samples; } }

    internal void Accept(ReadOnlySpan<byte> bytes, WaveFormat format, int channel, DateTime utc)
    {
        if (format.SampleRate != 48000 || channel < 0 || channel >= format.Channels ||
            bytes.Length % format.BlockAlign != 0)
            throw new InvalidDataException("Station IF requires real 48 kHz PCM with a valid channel.");
        var standard = format is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : format;
        int width = format.BitsPerSample / 8;
        if (!((standard.Encoding == WaveFormatEncoding.IeeeFloat && width == 4) ||
            (standard.Encoding == WaveFormatEncoding.Pcm && width is 2 or 3 or 4)))
            throw new InvalidDataException("Unsupported station IF sample format.");
        lock (gate)
        {
            if (lastUtc != default && (utc - lastUtc).TotalMilliseconds > 250 + bytes.Length * 1000.0 / format.BlockAlign / 48000)
                ResetLocked();
            for (int i = channel * width; i + width <= bytes.Length; i += format.BlockAlign)
            {
                var sample = bytes.Slice(i, width);
                float value = standard.Encoding == WaveFormatEncoding.IeeeFloat
                    ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample))
                    : width switch
                    {
                        2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                        3 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608f,
                        _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f
                    };
                ring[write] = float.IsFinite(value) ? value : 0;
                write = (write + 1) % ring.Length;
                used = Math.Min(used + 1, ring.Length);
                samples++;
            }
            lastUtc = utc;
        }
    }

    internal long Reset() { lock (gate) { ResetLocked(); return generation; } }

    private void ResetLocked()
    {
        generation++;
        used = write = 0;
        consumed = samples;
        lastUtc = default;
    }

    internal (float[] Pcm, DateTime Utc, long Generation)? Take(AfcTrackingProfile profile, DateTime now)
    {
        lock (gate)
        {
            if (used < profile.MinimumSamples || samples == consumed || now - lastUtc > TimeSpan.FromMilliseconds(500))
                return null;
            int n = Math.Min(used, profile.WindowSamples);
            var pcm = new float[n];
            int start = (write - n + ring.Length) % ring.Length;
            for (int i = 0; i < n; i++) pcm[i] = ring[(start + i) % ring.Length];
            consumed = samples;
            return (pcm, lastUtc.AddSeconds(-n / 96000.0), generation);
        }
    }
}
