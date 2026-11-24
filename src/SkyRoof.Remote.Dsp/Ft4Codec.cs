using System.Runtime.InteropServices;
using System.Text;
using VE3NEA;

namespace SkyRoof.Remote.Dsp;

// Uses the original SkyRoof native ABI; no UI or RF dependency.
// The Fortran decoder shares internal state, so serialize native calls.
internal static class Ft4Codec
{
    private static readonly object Gate = new();

    public static float[] Encode(string message, float audioHz)
    {
        var samples = new float[NativeFT4Coder.ENCODE_SAMPLE_COUNT];
        lock (Gate)
            NativeFT4Coder.encode_ft4(AsciiBuffer(message, NativeFT4Coder.ENCODE_MESSAGE_LENGTH), ref audioHz, samples);
        return samples;
    }

    public static string[] Decode(float[] samples, string myCall = "", string hisCall = "")
    {
        if (samples.Length != NativeFT4Coder.DECODE_SAMPLE_COUNT)
            throw new ArgumentException("FT4 requires exactly 290304 samples at 48 kHz.", nameof(samples));
        var messages = new List<string>();
        var stage = NativeFT4Coder.QsoStage.CALLING;
        var center = NativeFT4Coder.DEFAULT_AUDIO_FREQUENCY;
        var upper = 4000;
        NativeFT4Coder.DecodedMessageCallback callback = ptr =>
        {
            var message = Marshal.PtrToStringAnsi(ptr);
            if (!string.IsNullOrWhiteSpace(message)) messages.Add(message);
        };
        lock (Gate)
            NativeFT4Coder.decode_ft4(samples, ref stage, ref center, ref upper,
                AsciiBuffer(myCall, NativeFT4Coder.MAX_CALL_LENGTH),
                AsciiBuffer(hisCall, NativeFT4Coder.MAX_CALL_LENGTH), callback);
        GC.KeepAlive(callback);
        return messages.ToArray();
    }

    private static byte[] AsciiBuffer(string value, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > length) throw new ArgumentException("FT4 text exceeds native ABI field size.");
        var buffer = new byte[length + 1];
        bytes.CopyTo(buffer, 0);
        return buffer;
    }
}
