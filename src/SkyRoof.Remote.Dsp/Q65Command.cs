namespace SkyRoof.Remote.Dsp;

internal static class Q65Command
{
    public static void SelfTest()
    {
        foreach (string mode in Q65Codec.Submodes)
        {
            var geometry = Q65Codec.Geometry(mode);
            Console.WriteLine($"Q65 {mode}: {geometry.Seconds}s, spacing={geometry.ToneSpacing}, slot={geometry.SlotSamples12k}, TX start={geometry.TxStartSamples12k}");
        }
        RoundTrip("30A", "CQ K1ABC FN42");
        RoundTrip("60B", "W1XYZ K1ABC -15");
        RoundTrip("15A", "W1XYZ K1ABC R-40");
        Q65SlotSelfTest.Run();
        Q65AdvancedSelfTest.Run();
    }

    private static void RoundTrip(string mode, string message)
    {
        var geometry = Q65Codec.Geometry(mode);
        var waveform = Q65Codec.Encode(geometry, message, 1500, 0.5f, 12000);
        var slot = new short[geometry.SlotSamples12k];
        waveform.CopyTo(slot, geometry.TxStartSamples12k);
        using var decoder = new Q65Decoder(geometry);
        var rows = decoder.Decode(slot, 100);
        foreach (var row in rows) Console.WriteLine(row);
        if (!rows.Any(row => row.Text.Trim() == message))
            throw new InvalidDataException($"Q65 {mode} native roundtrip failed: {message}");
        Console.WriteLine($"PASS: Q65 {mode} native encode/decode, no RF.");
    }
}
