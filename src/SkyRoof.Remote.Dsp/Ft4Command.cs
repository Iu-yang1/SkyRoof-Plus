using System.Buffers.Binary;
using VE3NEA;

namespace SkyRoof.Remote.Dsp;

internal static class Ft4Command
{
    public static void DecodeFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != NativeFT4Coder.DECODE_SAMPLE_COUNT * 2)
            throw new InvalidDataException("Expected mono PCM16 LE, 48 kHz, 6.048 seconds.");
        var samples = new float[NativeFT4Coder.DECODE_SAMPLE_COUNT];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)) / 32768f;
        foreach (var message in Ft4Codec.Decode(samples)) Console.WriteLine(message);
    }

    public static void EncodeFile(string message, string path)
    {
        var samples = Ft4Codec.Encode(message, 1500);
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2),
                (short)Math.Clamp(Math.Round(samples[i] * 32768.0), -32768, 32767));
        File.WriteAllBytes(path, bytes);
    }

    public static void SelfTest()
    {
        var encoded = Ft4Codec.Encode("CQ K1ABC FN42", 1500);
        if (encoded.Any(s => !float.IsFinite(s)) || !encoded.Any(s => Math.Abs(s) > 0.001f))
            throw new InvalidDataException("Native FT4 encoder did not produce a finite waveform.");
        var window = new float[NativeFT4Coder.DECODE_SAMPLE_COUNT];
        encoded.CopyTo(window, 24000);
        var messages = Ft4Codec.Decode(window);
        foreach (var message in messages) Console.WriteLine(message);
        if (!messages.Any(m => m.Contains("K1ABC", StringComparison.Ordinal) &&
                               m.Contains("FN42", StringComparison.Ordinal)))
            throw new InvalidDataException("Native FT4 encode/decode round-trip did not recover the test message.");
        Console.WriteLine("PASS: original FT4 native codec round-trip, no RF transmission.");
        Ft4WindowSelfTest.Run(encoded);
    }
}
