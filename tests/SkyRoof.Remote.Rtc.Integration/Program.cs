using System.Diagnostics;
using System.Buffers.Binary;
using SkyRoof.Remote.SignalBus;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
string name = Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_BUS") ??
    throw new InvalidOperationException("RTC fixture bus missing.");
using var ring = new NamedSignalRing(name, 64, 4096, 42);
using var clock = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
var pcm = new byte[1920];
for (int offset = 0; offset < pcm.Length; offset += 2)
    BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset), 1234);
ulong first = 0;
while (await clock.WaitForNextTickAsync())
{
    ring.Publish(first, Stopwatch.GetTimestamp(), 48000, 960, pcm);
    first += 960;
}
