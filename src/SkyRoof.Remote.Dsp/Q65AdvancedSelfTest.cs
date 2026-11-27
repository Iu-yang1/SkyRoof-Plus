using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Dsp;

internal static class Q65AdvancedSelfTest
{
    internal static void Run()
    {
        var geometry = Q65Codec.Geometry("60B");
        var config = new Q65RxConfiguration { Enabled = true, Submode = "60B",
            RxHz = 1500, ToleranceHz = 100, MyCall = "K1ABC", HisCall = "W1XYZ", HisGrid = "FN42",
            ApMode = 2, QsoProgress = 2, Averaging = true, EmeDelay = true, MaxDrift = 5 };
        using var decoder = new Q65Decoder(geometry, config);
        const string message = "K1ABC W1XYZ -15";
        var waveform = Q65Codec.Encode(geometry, message, 1500, 0.5f, 12000);
        var slot = new short[geometry.SlotSamples12k];
        // Simulated 2.5 s round-trip lunar path plus native TX start.
        waveform.CopyTo(slot, geometry.TxStartSamples12k + 30000);
        var rows = decoder.Decode(slot, 100);
        if (!rows.Any(row => row.Text.Trim() == message))
            throw new InvalidDataException("Q65 native AP/EME delay configuration did not recover shifted frame.");
        config.FadingB90Ts = 1;
        config.Lorentzian = true;
        config.Pileup = true;
        using var options = new Q65Decoder(geometry, config);
        var pileupRows = options.Decode(slot, 100);
        if (!pileupRows.Any(row => row.Text.Trim() == message))
            throw new InvalidDataException("Q65 pileup AP hint did not recover directed message.");
        Console.WriteLine("PASS: native Q65 60B AP context, EME-delayed roundtrip, averaging/drift and fading/pileup options accepted.");
    }
}
