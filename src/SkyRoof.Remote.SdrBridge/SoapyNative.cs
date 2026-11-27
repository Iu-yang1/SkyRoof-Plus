using System.Runtime.InteropServices;

namespace SkyRoof.Remote.SdrBridge;

// SoapySDR 0.8 C ABI; Windows C long is 32 bits, long long is 64 bits.
internal static unsafe class SoapyNative
{
    private const string Library = "SoapySDR";
    internal const int Rx = 1;
    [StructLayout(LayoutKind.Sequential)]
    internal struct Kwargs { internal nuint Size; internal nint Keys, Values; }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SoapySDR_getABIVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_lastStatus();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SoapySDRDevice_lastError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern nint SoapySDRDevice_enumerateStrArgs(string args, out nuint length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SoapySDRKwargsList_clear(nint args, nuint length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern nint SoapySDRDevice_makeStrArgs(string args);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_unmake(nint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_setSampleRate(nint device, int direction, nuint channel, double rate);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern double SoapySDRDevice_getSampleRate(nint device, int direction, nuint channel);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_setFrequency(nint device, int direction, nuint channel, double frequency, nint args);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern double SoapySDRDevice_getFrequency(nint device, int direction, nuint channel);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_setGain(nint device, int direction, nuint channel, double gain);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern nint SoapySDRDevice_setupStream(nint device, int direction, string format, nuint* channels, nuint count, nint args);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_activateStream(nint device, nint stream, int flags, long timeNs, nuint count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_readStream(nint device, nint stream, void** buffers, nuint count, out int flags, out long timeNs, int timeoutUs);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_deactivateStream(nint device, nint stream, int flags, long timeNs);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SoapySDRDevice_closeStream(nint device, nint stream);

    internal static string Abi()
    {
        Environment.SetEnvironmentVariable("SOAPY_SDR_ROOT", AppContext.BaseDirectory);
        string plugins = Path.Combine(AppContext.BaseDirectory, "lib", "SoapySDR", "modules0.8");
        Environment.SetEnvironmentVariable("PATH", plugins + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        return Marshal.PtrToStringAnsi(SoapySDR_getABIVersion()) ?? throw new InvalidOperationException("Missing SoapySDR ABI.");
    }

    internal static void Check(int status, string operation)
    {
        if (status != 0)
            throw new IOException($"{operation}: {Marshal.PtrToStringAnsi(SoapySDRDevice_lastError())} ({status})");
    }
}
