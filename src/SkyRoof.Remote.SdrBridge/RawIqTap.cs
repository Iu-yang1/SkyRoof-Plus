using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkyRoof.Remote.SignalBus;

namespace SkyRoof.Remote.SdrBridge;

internal sealed class RawIqTap : IDisposable
{
    private readonly NamedSignalRing ring;
    private ulong next;

    internal RawIqTap(string name)
    {
        ring = new NamedSignalRing(name, 256, 65536, NewEpoch());
    }

    private static ulong NewEpoch() => (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    internal void Reset()
    {
        ring.ResetEpoch(NewEpoch());
        next = 0;
    }

    internal void Publish(ReadOnlySpan<float> interleaved, int rate)
    {
        if (interleaved.IsEmpty) return;
        if (interleaved.Length % 2 != 0 || rate <= 0)
            throw new ArgumentException("Raw IQ requires complete complex samples and actual sample rate.");
        for (int offset = 0; offset < interleaved.Length; offset += 16384)
        {
            int count = Math.Min(16384, interleaved.Length - offset);
            byte[] bytes = MemoryMarshal.AsBytes(interleaved.Slice(offset, count)).ToArray();
            ring.Publish(next, Stopwatch.GetTimestamp(), rate, count / 2, bytes);
            next += (ulong)(count / 2);
        }
    }

    public void Dispose() => ring.Dispose();
}
