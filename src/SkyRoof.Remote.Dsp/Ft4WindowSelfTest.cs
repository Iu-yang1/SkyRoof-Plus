using System.Buffers.Binary;
using SkyRoof.Remote.SignalBus;
using VE3NEA;

namespace SkyRoof.Remote.Dsp;

internal static class Ft4WindowSelfTest
{
    public static void Run(float[] encoded)
    {
        var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var assembler = new Ft4SlotAssembler();
        var windows = new List<Ft4SlotWindow>();
        var waveform = new float[360000];
        encoded.CopyTo(waveform, 24000);
        Feed(assembler, waveform, utc, 1, false, windows);
        if (windows.Count != 1 || windows[0].SlotUtcTicks != utc.Ticks ||
            windows[0].Samples.Length != NativeFT4Coder.DECODE_SAMPLE_COUNT ||
            !Ft4Codec.Decode(windows[0].Samples).Any(s => s.Contains("K1ABC", StringComparison.Ordinal)))
            throw new InvalidDataException("FT4 sample-clock slot round trip failed.");
        windows.Clear();
        Feed(assembler, waveform, utc.AddSeconds(7.5), 2, true, windows);
        if (windows.Count != 0)
            throw new InvalidDataException("FT4 decoded a slot interrupted by missing PCM.");
        Feed(assembler, waveform, utc.AddSeconds(15), 3, false, windows);
        if (windows.Count != 1 || windows[0].SourceEpoch != 3)
            throw new InvalidDataException("FT4 failed to reset after source epoch change.");
        Console.WriteLine("PASS: FT4 UTC-aligned sample window, native decode, gap discard, epoch reset.");
    }

    private static void Feed(Ft4SlotAssembler assembler, float[] samples, DateTime utc,
        ulong epoch, bool gap, List<Ft4SlotWindow> windows)
    {
        for (int start = 0; start < samples.Length; start += 960)
        {
            if (gap && start == 96000) continue;
            byte[] pcm = new byte[1920];
            for (int i = 0; i < 960; i++)
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2),
                    (short)Math.Clamp(Math.Round(samples[start + i] * 32768), -32768, 32767));
            assembler.Accept(new SignalFrame(start / 960, epoch, (ulong)start, 0, 48000, 960, pcm),
                1, utc.AddSeconds(start / 48000.0), windows.Add);
        }
    }
}
