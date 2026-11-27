using NAudio.Wave;
using SkyRoof.Remote.Ipc;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Remote.Dsp;

internal static class TelemetryCommand
{
    private sealed record Fixture(string File, string Modulation, string Framing, double Baud, string Input, double Deviation, int Length);

    public static void VerifyRecordedAudio(string directory)
    {
        Fixture[] fixtures =
        [
            new("ao73.wav", "BPSK", "AO40FEC", 1200, "tone-audio", 0, 256),
            new("ao27.wav", "AFSK", "AX25G3RUH", 1200, "fm-discriminator", 500, 0),
            new("aistechsat3.wav", "FSK", "AX100ASM", 4800, "fm-discriminator", 1200, 0),
            new("suomi_100.wav", "GMSK", "AX100ASM", 9600, "fm-discriminator", 2400, 0)
        ];
        foreach (var fixture in fixtures) Verify(directory, fixture);
    }

    private static void Verify(string directory, Fixture fixture)
    {
        using var audio = new AudioFileReader(Path.Combine(directory, fixture.File));
        if (audio.WaveFormat.SampleRate != 48000 || audio.WaveFormat.Channels != 1)
            throw new InvalidDataException("Expected original 48 kHz mono satellite recording.");
        int verified = 0;
        using var modem = new TelemetryModem(new TelemetryRxConfiguration { Enabled = true,
            Modulation = fixture.Modulation, Framing = fixture.Framing, Baud = fixture.Baud, Input = fixture.Input,
            CenterHz = 1700, DeviationHz = fixture.Deviation, Differential = fixture.Modulation == "BPSK" },
            (frame, inner) => { if (!inner && frame.CrcValid == true && (fixture.Length == 0 || frame.Bytes.Length == fixture.Length)) verified++; });
        var samples = new float[4096];
        int count;
        while ((count = audio.Read(samples, 0, samples.Length)) > 0) modem.Push(samples.AsSpan(0, count));
        modem.Flush();
        if (verified == 0) throw new InvalidDataException($"Remote telemetry {fixture.File}: no verified frame.");
        Console.WriteLine($"PASS: Remote telemetry original RF recording {fixture.File}: {verified} CRC/FEC verified frames.");
    }
}
