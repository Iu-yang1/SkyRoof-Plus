using System.Diagnostics;
using System.Reflection;
using SkyRoof.Remote.SignalBus;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
if (args is ["reader", var readName])
{
    using var reader = new NamedSignalRing(readName, 8, 128);
    var cursor = new SignalCursor();
    for (var i = 0; i < 3; i++)
    {
        Check(reader.TryReadNext(cursor, out var frame));
        Check(frame!.Sequence == i && frame.StreamEpoch == 101 && frame.Payload[0] == i);
    }
    Check(!reader.TryReadNext(cursor, out _));
    Console.WriteLine("PASS: independent child process read 3 signal frames.");
    return;
}

var name = "sim-" + Guid.NewGuid().ToString("N");
using var ring = new NamedSignalRing(name, 8, 128, 101);
for (var i = 0; i < 3; i++)
    Check(ring.Publish((ulong)i * 480, Stopwatch.GetTimestamp(), 48000, 480,
        [(byte)i, (byte)(255 - i)]) == i);
var startInfo = new ProcessStartInfo("dotnet")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
startInfo.ArgumentList.Add("reader");
startInfo.ArgumentList.Add(name);
using var child = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot launch reader.");
var output = child.StandardOutput.ReadToEnd();
var error = child.StandardError.ReadToEnd();
Check(child.WaitForExit(10_000) && child.ExitCode == 0);
Check(output.Contains("independent child process read 3 signal frames", StringComparison.Ordinal));
Console.WriteLine(output.Trim());
if (error.Length > 0) Console.Error.WriteLine(error);

var slow = new SignalCursor();
for (var i = 3; i < 14; i++)
    ring.Publish((ulong)i * 480, Stopwatch.GetTimestamp(), 48000, 480, [(byte)i]);
Check(ring.TryReadNext(slow, out var oldest));
Check(slow.LostFrames == 6 && oldest!.Sequence == 6);
for (var i = 7; i < 14; i++)
{
    Check(ring.TryReadNext(slow, out var next));
    Check(next!.Sequence == i && next.FirstSampleIndex == (ulong)i * 480);
}
Check(!ring.TryReadNext(slow, out _));
Console.WriteLine("PASS: slow-reader overrun reports six lost frames and preserves sample indices.");

ring.ResetEpoch(102);
ring.Publish(0, Stopwatch.GetTimestamp(), 12000, 120, [7, 8, 9]);
Check(ring.TryReadNext(slow, out var restarted));
Check(restarted!.Sequence == 0 && restarted.StreamEpoch == 102);
Check(restarted.SampleRate == 12000 && restarted.Payload[2] == 9);
Console.WriteLine("PASS: producer restart epoch changes and reader reattaches correctly.");

for (var i = 1; i <= 10000; i++)
    ring.Publish((ulong)i * 120, Stopwatch.GetTimestamp(), 12000, 120, [1, 2]);
var now = new SignalCursor();
Check(ring.TryReadNext(now, out var retained));
Check(retained!.Sequence == 9993 && now.LostFrames == 9993);
Console.WriteLine("PASS: 10000 published frames, deterministic wraparound/overrun recovery.");
static void Check(bool valid)
{
    if (!valid) throw new InvalidOperationException("Signal bus simulation assertion failed.");
}
