using System.Buffers.Binary;
using VE3NEA.SkySSTV;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Remote.Dsp;

internal static class TelemetryMediaSelfTest
{
    internal static void Run(string corpus)
    {
        var products = new List<TelemetryMediaProduct>();
        var signal = new SignalParams(800, Modulation.FSK, Framing.HADES, 48000, 800);
        using (var assembler = new TelemetryMediaAssembler(signal, 68446, products.Add))
        {
            byte[] raw = File.ReadAllBytes(Path.Combine(corpus, "hades-sa_img235_complete.ssdv"));
            for (int offset = 0; offset < raw.Length; offset += 256)
                assembler.Push(new Frame { Bytes = raw.AsSpan(offset + 5, 251).ToArray(), Framing = Framing.HADES, CrcValid = null });
            assembler.Push(VoiceFrame(0));
            assembler.Push(VoiceFrame(2));
            assembler.Flush();
        }
        var jpeg = products.Single(p => p.Extension == "jpg");
        if (!jpeg.Complete || jpeg.Received != 15 || !jpeg.Bytes.SequenceEqual(File.ReadAllBytes(
            Path.Combine(corpus, "hades-sa_img235_complete.ref.jpg"))))
            throw new InvalidDataException("Real HADES-SA SSDV JPEG differs from pinned reference.");
        var voice = products.Single(p => p.Extension == "wav");
        if (voice.Complete || voice.Received != 2 || voice.Expected != 3 ||
            !voice.Bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(voice.Bytes.AsSpan(24, 4)) != 8000)
            throw new InvalidDataException("Native Codec2 WAV or gap accounting differs.");
        Console.WriteLine("PASS: real off-air HADES-SA SSDV exact reference JPEG, native Codec2 WAV and missing sub-frame accounting.");
        VerifySstv();
    }

    private static Frame VoiceFrame(int index)
    {
        byte[] bytes = new byte[37];
        bytes[0] = 0xb3;
        bytes[1] = (byte)index;
        for (int k = 2; k < bytes.Length; k++) bytes[k] = (byte)(k * 7);
        return new Frame { Bytes = bytes, Framing = Framing.HADES, CrcValid = null, TimeSeconds = 0.4 * index };
    }

    private static void VerifySstv()
    {
        var source = new RgbImage(320, 240);
        for (int y = 0; y < 240; y++)
            for (int x = 0; x < 320; x++)
                source.Set(x, y, (byte)(x < 160 ? 255 : 0), (byte)(y < 120 ? 255 : 0), 64);
        var iq = SstvEncoder.Encode(source, SstvMode.Robot36, new SstvEncoderOptions { IncludeVis = true });
        var products = new List<TelemetryMediaProduct>();
        using (var assembler = new TelemetrySstvAssembler(products.Add))
        {
            for (int offset = 0; offset < iq.Length; offset += 4096)
                assembler.PushIq(iq.AsSpan(offset, Math.Min(4096, iq.Length - offset)));
            assembler.Flush();
        }
        var png = products.Single();
        if (png.Extension != "png" || png.Received < 216 || png.Expected != 240 ||
            !png.Bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Native SSTV Robot36 PNG or row count mismatch.");
        Console.WriteLine($"PASS: native streaming Robot36 SSTV generated IQ -> PNG, {png.Received}/{png.Expected} rows.");
    }
}
