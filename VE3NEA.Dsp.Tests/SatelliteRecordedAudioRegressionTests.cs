using MathNet.Numerics;
using NAudio.Wave;
using SkyRoof;
using VE3NEA.SkyTlm.Core;
using Xunit;

namespace VE3NEA.Dsp.Tests;

/// <summary>
/// Over-the-air 48-kHz real audio corpus from daniestevez/satellite-recordings.
/// Exercises SkyRoof's production Hilbert/AF-to-IQ bridge and the real SkyTlm
/// BPSK burst/FEC chain; no synthetic modulation or substituted soft symbols.
/// </summary>
public sealed class SatelliteRecordedAudioRegressionTests
{
    private const int SampleRate = 48000;

    [Fact]
    [Trait("Category", "RecordedSatelliteAudio")]
    public void Ao73_RecordedAf_Produces256ByteAo40FecFrame()
    {
        string? folder = Environment.GetEnvironmentVariable("SKYROOF_SATELLITE_AUDIO_CORPUS");
        if (string.IsNullOrWhiteSpace(folder)) return;

        string path = Path.Combine(folder, "ao73.wav");
        Assert.True(File.Exists(path), "Missing public FUNcube-1 / AO-73 RF audio sample: " + path);
        float[] pcm = ReadMono48k(path);
        Assert.True(pcm.Length > SampleRate * 4, "AO-40 FEC needs at least 5200 / 1200 = 4.33s of data");

        // Produce the analytic signal ONCE. Replaying it with a different NCO
        // centre is equivalent to the operator's AF-centre setting and does
        // not alter the original waveform or manufacture frame bits.
        Complex32[] analytic = new TelemetryRealAudioIqConverter(1).Convert(pcm);
        int[] centers = { 1700, 12000, 1500, 10000, 2000, 14000, 5000, 8000,
                          3000, 16000, 4000, 6000, 18000, 1000, 22000 };
        var measurements = new List<string>();
        bool succeeded = false;

        foreach (int center in centers)
        {
            var iq = Translate(analytic, center - 1);
            var parameters = new SignalParams(
                1200, Modulation.BPSK, Framing.AO40FEC, SampleRate)
            {
                Differential = true
            };
            var frames = new List<Frame>();
            int bursts = 0;
            int symbols = 0;
            using (var pipeline = new StreamingPipeline(parameters))
            {
                pipeline.BurstDecoded += report =>
                {
                    bursts++;
                    symbols += report.Soft.Count;
                    frames.AddRange(report.Frames);
                };
                for (int at = 0; at < iq.Length; at += 4096)
                    pipeline.Push(iq.AsSpan(at, Math.Min(4096, iq.Length - at)));
                pipeline.Flush();
            }
            int verified = frames.Count(frame =>
                frame.Framing == Framing.AO40FEC &&
                frame.CrcValid == true && frame.Bytes.Length == 256);
            measurements.Add($"center={center} Hz bursts={bursts} symbols={symbols} verified={verified}");
            if (verified > 0)
            {
                succeeded = true;
                break;
            }
        }

        foreach (string row in measurements)
            Console.WriteLine("SAT_AUDIO AO73 " + row);
        Assert.True(succeeded,
            "AO-73 48kHz RF audio did not yield a valid AO-40 FEC/RS frame. " +
            string.Join("; ", measurements));
    }

    private static float[] ReadMono48k(string path)
    {
        using var audio = new AudioFileReader(path);
        Assert.Equal(SampleRate, audio.WaveFormat.SampleRate);
        Assert.Equal(1, audio.WaveFormat.Channels);
        var data = new List<float>((int)(audio.Length / audio.WaveFormat.BlockAlign));
        float[] block = new float[8192];
        int n;
        while ((n = audio.Read(block, 0, block.Length)) > 0)
            data.AddRange(block.AsSpan(0, n).ToArray());
        return data.ToArray();
    }

    private static Complex32[] Translate(Complex32[] analytic, int hz)
    {
        double delta = -2 * Math.PI * hz / SampleRate;
        double phase = 0;
        var output = new Complex32[analytic.Length];
        for (int i = 0; i < output.Length; i++)
        {
            double cos = Math.Cos(phase), sin = Math.Sin(phase);
            output[i] = new Complex32(
                (float)(analytic[i].Real * cos - analytic[i].Imaginary * sin),
                (float)(analytic[i].Real * sin + analytic[i].Imaginary * cos));
            phase += delta;
            if (phase < -Math.PI) phase += 2 * Math.PI;
        }
        return output;
    }
}
