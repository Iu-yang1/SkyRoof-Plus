using SkyRoof.Remote.Foundation.Digital;
using VE3NEA;

namespace SkyRoof.Remote.Dsp;

internal static class Ft4DirectedSelfTest
{
    public static void Run()
    {
        foreach (string exchange in new[] { "FN31", "-09", "R-09", "RR73", "73" })
        {
            var window = new float[NativeFT4Coder.DECODE_SAMPLE_COUNT];
            Ft4Codec.Encode("K1ABC W1XYZ " + exchange, 1500).CopyTo(window, 24000);
            bool recovered = Ft4Codec.Decode(window, "K1ABC", "W1XYZ")
                .Select(Ft4NativeContactParser.Parse)
                .Any(x => x is { To: "K1ABC", From: "W1XYZ" } && x.Exchange == exchange);
            if (!recovered) throw new InvalidDataException("Native FT4 contact parser round-trip failed: " + exchange);
        }
        Console.WriteLine("PASS: real native FT4 directed grid, reports, RR73 and 73 decoded by automatic exchange parser.");
    }
}
