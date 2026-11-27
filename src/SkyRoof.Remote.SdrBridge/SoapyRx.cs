using System.Runtime.InteropServices;
using static SkyRoof.Remote.SdrBridge.SoapyNative;

namespace SkyRoof.Remote.SdrBridge;

internal sealed unsafe class SoapyRx : IDisposable
{
    private nint _device, _stream;
    private bool _active;
    internal int SampleRate { get; }
    internal double Frequency { get; }

    internal SoapyRx(string arguments, double frequency, int rate, double? gain)
    {
        _device = SoapySDRDevice_makeStrArgs(arguments);
        try
        {
            Check(SoapySDRDevice_lastStatus(), "make");
            if (_device == 0) throw new IOException("SoapySDR device unavailable.");
            Check(SoapySDRDevice_setSampleRate(_device, Rx, 0, rate), "setSampleRate");
            Check(SoapySDRDevice_setFrequency(_device, Rx, 0, frequency, 0), "setFrequency");
            if (gain.HasValue) Check(SoapySDRDevice_setGain(_device, Rx, 0, gain.Value), "setGain");
            double actual = SoapySDRDevice_getSampleRate(_device, Rx, 0);
            Check(SoapySDRDevice_lastStatus(), "getSampleRate");
            SampleRate = checked((int)Math.Round(actual));
            Frequency = SoapySDRDevice_getFrequency(_device, Rx, 0);
            Check(SoapySDRDevice_lastStatus(), "getFrequency");
            nuint channel = 0;
            _stream = SoapySDRDevice_setupStream(_device, Rx, "CF32", &channel, 1, 0);
            Check(SoapySDRDevice_lastStatus(), "setupStream");
            if (_stream == 0) throw new IOException("SoapySDR CF32 stream unavailable.");
            Check(SoapySDRDevice_activateStream(_device, _stream, 0, 0, 0), "activateStream");
            _active = true;
        }
        catch { Dispose(); throw; }
    }

    // -1: timeout; -4: overflow. An abrupt block is discarded rather than joined to a valid epoch.
    internal int Read(float[] data)
    {
        fixed (float* samples = data)
        {
            void* buffer = samples;
            int result = SoapySDRDevice_readStream(_device, _stream, &buffer, (nuint)(data.Length / 2), out int flags, out _, 200000);
            if (result > 0 && (flags & 8) != 0) return -4;
            if (result > data.Length / 2) throw new InvalidDataException("Invalid native sample count.");
            if (result < 0 && result is not (-1 or -4)) Check(result, "readStream");
            return result;
        }
    }

    internal static IReadOnlyList<Dictionary<string, string>> Enumerate(string arguments)
    {
        var pointer = SoapySDRDevice_enumerateStrArgs(arguments, out nuint count);
        try
        {
            Check(SoapySDRDevice_lastStatus(), "enumerate");
            var devices = new List<Dictionary<string, string>>();
            int size = Marshal.SizeOf<Kwargs>();
            for (nuint index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<Kwargs>(pointer + checked((int)index * size));
                var values = new Dictionary<string, string>();
                for (nuint k = 0; k < item.Size; k++)
                {
                    int offset = checked((int)k * nint.Size);
                    string key = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(item.Keys, offset)) ?? "";
                    string value = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(item.Values, offset)) ?? "";
                    values[key] = value;
                }
                devices.Add(values);
            }
            return devices;
        }
        finally { if (pointer != 0) SoapySDRKwargsList_clear(pointer, count); }
    }

    public void Dispose()
    {
        if (_active) { SoapySDRDevice_deactivateStream(_device, _stream, 0, 0); _active = false; }
        if (_stream != 0) { SoapySDRDevice_closeStream(_device, _stream); _stream = 0; }
        if (_device != 0) { SoapySDRDevice_unmake(_device); _device = 0; }
    }
}
