using System.Buffers.Binary;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.Dsp;

internal static class Q65SlotSelfTest
{
    public static void Run()
    {
        var geometry = Q65Codec.Geometry("15A");
        var utc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = new short[geometry.Seconds * 48000];
        var tx = Q65Codec.Encode(geometry, "CQ K1ABC FN42", 1500, 0.5f, 48000);
        tx.CopyTo(samples, geometry.TxStartSamples12k * 4);
        var assembler = new Q65SlotAssembler(geometry.Seconds);
        var windows = new List<Q65Slot>();
        Feed(assembler, samples, utc, 1, false, windows);
        if (windows.Count != 1 || windows[0].Index != utc.ToUnixTimeSeconds() / geometry.Seconds ||
            !windows[0].Samples.AsSpan().SequenceEqual(samples))
            throw new InvalidDataException("Q65 UTC slot assembly changed PCM.");
        using var decoder = new Q65Decoder(geometry);
        if (!decoder.Decode(windows[0].Samples, windows[0].Index, 48000).Any(row => row.Text.Trim() == "CQ K1ABC FN42"))
            throw new InvalidDataException("Q65 48 kHz native decode did not recover the slot.");
        windows.Clear();
        Feed(assembler, samples, utc.AddSeconds(15), 2, true, windows);
        if (windows.Count != 0) throw new InvalidDataException("Q65 accepted a slot with missing PCM.");
        Feed(assembler, samples, utc.AddSeconds(30), 3, false, windows);
        if (windows.Count != 1 || windows[0].Epoch != 3)
            throw new InvalidDataException("Q65 epoch reset failed.");
        Console.WriteLine("PASS: Q65 48 kHz UTC slot, native resampling/decode, gap discard, source epoch reset.");
    }

    private static void Feed(Q65SlotAssembler assembler, short[] samples, DateTimeOffset utc,
        ulong epoch, bool gap, List<Q65Slot> windows)
    {
        for (int start = 0; start < samples.Length; start += 960)
        {
            if (gap && start == 96000) continue;
            byte[] pcm = new byte[1920];
            for (int i = 0; i < 960; i++)
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), samples[start + i]);
            assembler.Accept(new SignalFrame(start / 960, epoch, (ulong)start, 0, 48000, 960, pcm),
                1, utc.AddSeconds(start / 48000.0), windows.Add);
        }
    }
}
