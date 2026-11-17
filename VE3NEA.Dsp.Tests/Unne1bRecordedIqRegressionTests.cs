using System.Buffers.Binary;
using System.Text.Json;
using MathNet.Numerics;
using SkyRoof;
using VE3NEA.SkyTlm.Core;
using Xunit;

namespace VE3NEA.Dsp.Tests;

/// <summary>Actual 2026-10-04 over-the-air IQ through the same SkyTlm
/// StreamingPipeline -> BurstDecoded.Soft -> Unne1bDeframer path used in
/// TelemetryPanel. No synthesised frames, fake soft bits or mock demodulator.</summary>
public sealed class Unne1bRecordedIqRegressionTests
{
    private const int SourceRate = 50000;
    private const int ReceiverRate = 48000;
    private sealed record Fixture(string Name, int Type, uint? Clock,
        double RecordedCenterHz);
    private static readonly Fixture[] Fixtures =
    [
        new("pass_t211s_type01.iq", 1, 192304, -5289),
        new("pass_t032s_type14.iq", 14, 192124, -3087),
        new("pass_t122s_voice.iq", 15, null, -8437),
    ];
    private sealed record FrameMetric(int Type, bool? Crc, uint? Clock,
        int? VoiceNumber, int Length);
    private sealed record Attempt(string Name, double DeviationHz,
        double CenterHz, bool DefaultShapeGate, double Gain,
        double InputRms,
        int Bursts, int SoftBits, FrameMetric[] Frames,
        double[] CfoHz, double[] SnrDb,
        double PeakDetectorZ, double ThresholdAtPeak, int DetectorBurstFrames);

    [Fact]
    [Trait("Category", "RecordedRealIq")]
    public void RealUnne1bIq_ProductionFskPipeline_ToCrcAndFrame()
    {
        string? root = Environment.GetEnvironmentVariable("SKYROOF_UNNE_REAL_IQ_DIR");
        if (string.IsNullOrWhiteSpace(root)) return;
        Assert.True(Directory.Exists(root), "Missing downloaded real IQ corpus: " + root);
        var attempts = new List<Attempt>();
        var failures = new List<string>();
        foreach (Fixture fixture in Fixtures)
        {
            string file = Path.Combine(root, fixture.Name);
            Assert.True(File.Exists(file), "Missing real recording: " + file);
            Complex32[] recording = LoadIq(file);
            bool found = false;
            foreach (double deviation in new[] { 820.0, 562.5 })
            foreach (double residual in new[] { 0.0, -250.0, 250.0 })
            {
                double center = fixture.RecordedCenterHz + residual;
                Complex32[] tuned = ResampleIq(recording, center);
                double initialRms = Math.Sqrt(tuned.Average(x =>
                    (double)x.Real * x.Real + (double)x.Imaginary * x.Imaginary));
                // Receiver gain is a linear scaling of the unchanged over-
                // the-air IQ waveform, not manufactured modulation.
                foreach (double gain in new[] { 1.0,
                    0.1 / Math.Max(initialRms, 1e-10),
                    0.3 / Math.Max(initialRms, 1e-10) })
                {
                    Complex32[] input = gain == 1 ? tuned :
                        tuned.Select(x => new Complex32((float)(x.Real * gain),
                            (float)(x.Imaginary * gain))).ToArray();
                    foreach (bool defaultGate in new[] { true, false })
                    {
                        Attempt result = Decode(fixture.Name, input, deviation,
                            center, defaultGate, gain);
                        attempts.Add(result);
                        // A single valid voice header does not prove a working CODEC2
                        // stream. The reference capture contains all six
                        // consecutive subframes, which must be recovered
                        // within the SAME tuning/demodulation attempt.
                        found |= fixture.Type == 15
                            ? Enumerable.Range(0, 6).All(number =>
                                result.Frames.Any(frame =>
                                    frame.Type == 15 && frame.VoiceNumber == number))
                            : result.Frames.Any(frame =>
                                frame.Type == fixture.Type &&
                                frame.Crc == true && frame.Clock == fixture.Clock);
                    }
                }
            }
            if (!found) failures.Add("No expected verified frame in " + fixture.Name);
        }
        string json = JsonSerializer.Serialize(new
        {
            source_commit = "N6RFM/HADESx-Decoder 238386904c44a60657bac0039c2edb681446d2c0",
            sample_rate_original_hz = SourceRate,
            sample_rate_skyroof_hz = ReceiverRate,
            references = Fixtures,
            attempts
        }, new JsonSerializerOptions { WriteIndented = true });
        string? output = Environment.GetEnvironmentVariable("SKYROOF_UNNE_REAL_IQ_METRICS");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, json);
        }
        foreach (Attempt a in attempts)
            Console.WriteLine($"REAL_IQ {a.Name} dev={a.DeviationHz} " +
                $"center={a.CenterHz} defaultGate={a.DefaultShapeGate} " +
                $"gain={a.Gain:F2} rms={a.InputRms:E3} " +
                $"bursts={a.Bursts} soft={a.SoftBits} " +
                $"frames={string.Join(",", a.Frames.Select(f => $"{f.Type}:crc={f.Crc}:clk={f.Clock}:voice={f.VoiceNumber}"))} " +
                $"zPeak={a.PeakDetectorZ:F1} onZ={a.ThresholdAtPeak:F1} detectorFrames={a.DetectorBurstFrames}");
        Assert.True(failures.Count == 0,
            string.Join("; ", failures) + ". See uploaded real-IQ JSON metrics.");
    }

    private static Attempt Decode(string name, Complex32[] iq,
        double deviation, double center, bool defaultGate, double gain)
    {
        var frames = new List<FrameMetric>();
        var cfo = new List<double>();
        var snr = new List<double>();
        int bursts = 0, softBits = 0;
        double peakZ = 0, thresholdAtPeak = 0;
        int framesInBurst = 0;
        object sync = new();
        var parameters = new SignalParams(200, Modulation.FSK, Framing.HADES,
            ReceiverRate, deviation);
        double rms = Math.Sqrt(iq.Average(x =>
            (double)x.Real * x.Real + (double)x.Imaginary * x.Imaginary));
        // Compare production gate defaults with the diagnostic shape gate
        // bypass. If only bypass works, fix UNNE production configuration.
        // The pinned SkySignals fork has a native UNNE-1B framing override;
        // use reflection solely so the regular SkyRoof compile job (which
        // builds against its existing vendor DLL) can still compile this
        // draft integration test. The real-IQ job replaces that DLL first.
        var opts = defaultGate ? new StreamingOptions() : new StreamingOptions
        {
            GateByShape = false,
            DecodeRejected = true,
            MaxBurstSeconds = 16,
            HangoverMs = 250,
            GuardMs = 160
        };
        Type? decoderType = typeof(StreamingPipeline).Assembly.GetType(
            "VE3NEA.SkyTlm.Deframing.Unne1bDeframer");
        var overrideProp = typeof(StreamingOptions).GetProperty("DeframerOverride");
        Assert.NotNull(decoderType);
        Assert.NotNull(overrideProp);
        overrideProp!.SetValue(opts, Activator.CreateInstance(decoderType!));
        typeof(StreamingOptions).GetProperty("RecoverFadedTail")?.SetValue(opts, true);
        using (var pipeline = new StreamingPipeline(parameters, opts))
        {
            // One diagnostic group per candidate frequency/deviation at native
            // gain: locate missed onset versus demod/deframing failure.
            if (!defaultGate && Math.Abs(gain - 1.0) < 1e-6)
              pipeline.DetectionFrameProcessed += frame =>
              {
                  if (frame.InBurst) framesInBurst++;
                  if (frame.ZStat > peakZ)
                  {
                      peakZ = frame.ZStat;
                      thresholdAtPeak = pipeline.OnSigma;
                  }
              };
            pipeline.BurstDecoded += report =>
            {
                lock (sync)
                {
                    bursts++;
                    softBits += report.Soft.Soft.Length;
                    cfo.Add(report.Burst.CfoHz);
                    snr.Add(report.Burst.SnrDb);
                    foreach (Frame frame in report.Frames)
                    {
                        int type = frame.Bytes[0] >> 4;
                        uint? clock = type == 15 || frame.Bytes.Length < 5
                            ? null : BinaryPrimitives.ReadUInt32LittleEndian(
                                frame.Bytes.AsSpan(1, 4));
                        frames.Add(new FrameMetric(type, frame.CrcValid, clock,
                            type == 15 ? frame.Bytes[1] : null, frame.Bytes.Length));
                    }
                }
            };
            for (int i = 0; i < iq.Length; i += 4096)
            {
                var samples = new Complex32[Math.Min(4096, iq.Length - i)];
                Array.Copy(iq, i, samples, 0, samples.Length);
                pipeline.Push(samples);
            }
            // Simulate silence between satellite's 30-second transmissions.
            for (int i = 0; i < 14; i++)
                pipeline.Push(new Complex32[4096]);
        }
        lock (sync)
            return new Attempt(name, deviation, center, defaultGate, gain, rms,
                bursts, softBits,
                frames.ToArray(), cfo.ToArray(), snr.ToArray(),
                peakZ, thresholdAtPeak, framesInBurst);
    }

    private static Complex32[] LoadIq(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Assert.True(data.Length > 80000 && data.Length % 8 == 0,
            "Expected native little-endian complex float32 IQ: " + path);
        var iq = new Complex32[data.Length / 8];
        for (int i = 0; i < iq.Length; i++)
        {
            var sample = data.AsSpan(i * 8, 8);
            iq[i] = new Complex32(
                BinaryPrimitives.ReadSingleLittleEndian(sample[..4]),
                BinaryPrimitives.ReadSingleLittleEndian(sample[4..]));
        }
        return iq;
    }

    private static Complex32[] ResampleIq(Complex32[] source,
        double recordedCenterHz)
    {
        int count = (int)Math.Floor((source.Length - 1.0) * ReceiverRate / SourceRate);
        var output = new Complex32[count];
        for (int i = 0; i < count; i++)
        {
            double pos = (double)i * SourceRate / ReceiverRate;
            int j = (int)pos;
            double frac = pos - j;
            double re = source[j].Real * (1 - frac) + source[j + 1].Real * frac;
            double im = source[j].Imaginary * (1 - frac) + source[j + 1].Imaginary * frac;
            double phase = -2 * Math.PI * recordedCenterHz * i / ReceiverRate;
            double cos = Math.Cos(phase), sin = Math.Sin(phase);
            output[i] = new Complex32(
                (float)(re * cos - im * sin), (float)(re * sin + im * cos));
        }
        return output;
    }
}
