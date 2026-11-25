using System.Runtime.InteropServices;
using System.Text;
using static SkyRoof.Remote.Dsp.MfskQ65Abi;

namespace SkyRoof.Remote.Dsp;

internal sealed record Q65Geometry(string Submode, uint Mode, int Seconds, int SlotSamples12k,
    int TxStartSamples12k, float ToneSpacing);
internal sealed record Q65Decode(string Text, float FrequencyHz, float DtSeconds, float SnrDb,
    byte Flags, byte ApType);

internal static unsafe class Q65Codec
{
    private static readonly string[] Modes = ["15A", "30A", "60A", "60B", "60C", "60D", "60E", "120D", "120E", "300A"];
    internal static IReadOnlyList<string> Submodes => Modes;

    public static Q65Geometry Geometry(string submode)
    {
        if (mfsk_abi_version() != 3) throw new InvalidDataException("Q65 requires mfsk C ABI v3.");
        int index = Array.IndexOf(Modes, submode.ToUpperInvariant());
        if (index < 0) throw new ArgumentException("Unknown Q65 submode.");
        uint mode = (uint)(10 + index);
        var info = new ModeInfo { Size = (uint)sizeof(ModeInfo) };
        Check(mfsk_mode_info(mode, ref info));
        if (info.Mode != mode || info.Tones != 65 || info.Symbols != 85 ||
            info.SlotSamples != (uint)(info.SlotSeconds * 12000))
            throw new InvalidDataException("Unexpected Q65 native geometry.");
        return new(submode, mode, (int)info.SlotSeconds, (int)info.SlotSamples,
            (int)info.TxBufferStart, info.ToneSpacing);
    }

    public static short[] Encode(Q65Geometry geometry, string text, float frequency, float gain, uint rate)
    {
        if (rate is not (12000 or 48000) || !float.IsFinite(frequency) || frequency <= 0 ||
            frequency + 64 * geometry.ToneSpacing >= rate / 2f ||
            !float.IsFinite(gain) || gain is < 0 or > 1 || string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Invalid Q65 waveform request.");
        var samples = new float[checked(geometry.Seconds * (int)rate)];
        nuint count;
        fixed (float* output = samples)
            Check(mfsk_encode(geometry.Mode, text, 0, rate, frequency, output, (nuint)samples.Length, out count));
        if (count == 0 || count > (nuint)samples.Length) throw new InvalidDataException("Invalid Q65 sample count.");
        var pcm = new short[(int)count];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (short)Math.Clamp(Math.Round(samples[i] * gain * 32768.0), -32768, 32767);
        return pcm;
    }

    internal static void Check(int status, nint decoder = 0)
    {
        if (status == 0) return;
        nint error = decoder == 0 ? mfsk_last_error() : mfsk_decoder_last_error(decoder);
        throw new InvalidDataException($"mfsk status {status}: {Marshal.PtrToStringUTF8(error)}");
    }
}
