using NAudio.Wave;
using SkyRoof.Remote.Ipc;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Remote.Dsp;

internal static class TelemetryCommand
{
    public static void VerifyRecordedAudio(string directory)
    {
        Verify(directory, "ao73.wav", "BPSK", "AO40FEC", 1200, "tone-audio", 1700, 0, 256);
        Verify(directory, "ao27.wav", "AFSK", "AX25G3RUH", 1200, "fm-discriminator", 1700, 500, 0);
        Verify(directory, "aistechsat3.wav", "FSK", "AX100ASM", 4800, "fm-discriminator", 1700, 1200, 0);
        Verify(directory, "suomi_100.wav", "GMSK", "AX100ASM", 9600, "fm-discriminator", 1700, 2400, 0);
    }

    private static void Verify(string directory, string name, string modulation, string framing,
        double baud, string input, int center, double deviation, int length)
    {
        using var audio = new AudioFileReader(Path.Combine(directory, name));
        if (audio.WaveFormat.SampleRate != 48000 || audio.WaveFormat.Channels != 1)
            throw new InvalidDataException("Expected original 48 kHz mono satellite recording.");
        int verified = 0;
        using var modem = new TelemetryModem(new TelemetryRxConfiguration { Enabled = true,
            Modulation = modulation, Framing = framing, Baud = baud, Input = input,
            CenterHz = center, DeviationHz = deviation, Differential = modulation == "BPSK" },
            (frame, inner) => { if (!inner && frame.CrcValid == true && (length == 0 || frame.Bytes.Length == length)) verified++; });
        var samples = new float[4096];
        int count;
        while ((count = audio.Read(samples, 0, samples.Length)) > 0) modem.Push(samples.AsSpan(0, count));
        modem.Flush();
        if (verified == 0) throw new InvalidDataException($"Remote telemetry {name}: no verified frame.");
        Console.WriteLine($"PASS: Remote telemetry original RF recording {name}: {verified} CRC/FEC verified frames.");
    }
}
