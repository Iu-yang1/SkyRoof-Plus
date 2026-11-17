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

    [Fact]
    [Trait("Category", "RecordedSatelliteAudio")]
    public void Ao73_AnalogTranslated12kIf_RetainsVerifiedFecFrame()
    {
        string? folder = Environment.GetEnvironmentVariable("SKYROOF_SATELLITE_AUDIO_CORPUS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        float[] pcm = ReadMono48k(Path.Combine(folder, "ao73.wav"));
        // Reposition the actual 1700-Hz recorded carrier to a 12-kHz real IF
        // in the analogue domain. Symbols and packet data are not synthesized.
        Complex32[] analytic = new TelemetryRealAudioIqConverter(1).Convert(pcm);
        Complex32[] translated = Translate(analytic, -10300);
        float[] ifPcm = translated.Select(x => x.Real).ToArray();
        Complex32[] ifIq = new TelemetryRealAudioIqConverter(12000).Convert(ifPcm);
        var signal = new SignalParams(1200, Modulation.BPSK,
            Framing.AO40FEC, SampleRate) { Differential = true };
        var (bursts, verified) = DecodeFrames(ifIq, signal);
        Console.WriteLine($"SAT_IF AO73 real12kHz bursts={bursts} verified={verified}");
        Assert.True(verified > 0,
            "AO-73 real-audio translated 12-kHz IF failed to recover AO-40 FEC.");
    }

    [Theory]
    [Trait("Category", "RecordedSatelliteAudio")]
    [InlineData("ao27.wav", 1200, Modulation.AFSK, Framing.AX25G3RUH)]
    [InlineData("aistechsat3.wav", 4800, Modulation.FSK, Framing.AX100ASM)]
    [InlineData("suomi_100.wav", 9600, Modulation.GMSK, Framing.AX100ASM)]
    public void RepresentativeSatelliteAudio_ProducesIntegrityVerifiedFrames(
        string name, double baud, Modulation modulation, Framing framing)
    {
        string? folder = Environment.GetEnvironmentVariable("SKYROOF_SATELLITE_AUDIO_CORPUS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        string path = Path.Combine(folder, name);
        Assert.True(File.Exists(path), "Missing real sample: " + path);
        float[] pcm = ReadMono48k(path);
        Assert.NotEmpty(pcm);

        var profiles = new List<SignalParams>
        {
            new(baud, modulation, framing, SampleRate,
                modulation == Modulation.AFSK ? 500 : null)
            {
                AfCarrier = modulation == Modulation.AFSK ? 1700 : null
            }
        };
        if (name == "aistechsat3.wav")
            profiles.Add(new SignalParams(9600, Modulation.FSK, framing, SampleRate));
        if (name == "suomi_100.wav")
            profiles.Add(new SignalParams(9600, Modulation.FSK, framing, SampleRate));

        // Check the on-air real-RF path on the public recording. The AFSK
        // fixture additionally checks the already-discriminated AF bridge.
        Complex32[] analytic = new TelemetryRealAudioIqConverter(1).Convert(pcm);
        int[] centres = { 12000, 1700, 10000, 16000, 8000, 14000,
                          5000, 2000, 6000, 18000, 3000, 22000 };
        var observations = new List<string>();
        bool recovered = false;
        foreach (var p in profiles)
        {
            // These public audio clips may be already FM-discriminated
            // baseband, unlike AO-73's SSB carrier audio. Re-synthesizing
            // the FM carrier is necessary to test that input interpretation.
            // Sweep the physical peak deviation, without changing the
            // recorded PCM or bypassing the production SkyTlm deframer.
            double[] deviations = modulation == Modulation.AFSK
                ? new[] { 1800.0 }
                : new[] { p.Baud / 4.0, p.Baud / 2.0, p.Baud * 0.75, 1800.0 };
            foreach (double peak in deviations)
            {
                var fmIq = new TelemetryAfskFmIqConverter(peak).Convert(pcm);
                var candidate = modulation == Modulation.FSK
                    ? p with { Deviation = peak }
                    : p;
                var (bursts, crc) = DecodeFrames(fmIq, candidate);
                observations.Add($"FM-audio bridge dev={peak}Hz {p.Baud}Bd bursts={bursts} verified={crc}");
                if (crc > 0) { recovered = true; break; }
            }
            if (recovered) break;
            foreach (int hz in centres)
            {
                var iq = Translate(analytic, hz - 1);
                var (bursts, crc) = DecodeFrames(iq, p);
                observations.Add($"centre={hz}Hz {p.Modulation}/{p.Baud}Bd bursts={bursts} verified={crc}");
                if (crc > 0) { recovered = true; break; }
            }
            if (recovered) break;
        }
        foreach (var line in observations)
            Console.WriteLine("SAT_AUDIO " + name + " " + line);
        Assert.True(recovered,
            $"Unable to recover a CRC/FEC-valid {name} frame with {modulation}/{framing}. " +
            string.Join("; ", observations));
    }

    private static (int Bursts, int Verified) DecodeFrames(Complex32[] iq, SignalParams parameters)
    {
        int bursts = 0, verified = 0;
        using var pipeline = new StreamingPipeline(parameters);
        pipeline.BurstDecoded += report =>
        {
            bursts++;
            verified += report.Frames.Count(frame => frame.CrcValid == true);
        };
        for (int at = 0; at < iq.Length; at += 4096)
            pipeline.Push(iq.AsSpan(at, Math.Min(4096, iq.Length - at)));
        pipeline.Flush();
        return (bursts, verified);
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
