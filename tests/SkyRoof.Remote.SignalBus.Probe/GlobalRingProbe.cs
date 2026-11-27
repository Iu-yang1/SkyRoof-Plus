using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using SkyRoof.Remote.SignalBus;

[SupportedOSPlatform("windows")]
internal static class GlobalRingProbe
{
    internal static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("SKYROOF_SIGNALBUS_SCOPE", "global");
        switch (args)
        {
            case ["global-producer", var name, var sid, var ready, var stop]:
                Produce(name, sid, ready, stop);
                break;
            case ["global-reader", var name]:
                Read(name);
                break;
            case ["global-denied", var name]:
                VerifyDenied(name);
                break;
            default:
                throw new ArgumentException("Unknown global ring probe arguments.");
        }
    }

    private static void Produce(string name, string sid, string ready, string stop)
    {
        Environment.SetEnvironmentVariable("SKYROOF_SIGNALBUS_READER_SIDS", sid);
        using var ring = new NamedSignalRing(name, 8, 128, 101);
        ring.Publish(0, Stopwatch.GetTimestamp(), 48000, 480, [12, 34, 56]);
        File.WriteAllText(ready, Process.GetCurrentProcess().SessionId.ToString());
        var clock = Stopwatch.StartNew();
        while (!File.Exists(stop) && clock.Elapsed < TimeSpan.FromSeconds(60))
            Thread.Sleep(100);
    }

    private static void Read(string name)
    {
        using var ring = new NamedSignalRing(name, 8, 128);
        var cursor = new SignalCursor();
        if (!ring.TryReadNext(cursor, out var frame) || frame is null ||
            frame.StreamEpoch != 101 || frame.Sequence != 0 ||
            !frame.Payload.AsSpan().SequenceEqual(new byte[] { 12, 34, 56 }))
            throw new InvalidOperationException("Global SignalBus frame did not match.");
        VerifyWriteDenied(name);
        Console.WriteLine($"PASS: authorized read-only global ring; reader session {Process.GetCurrentProcess().SessionId}.");
    }

    private static void VerifyWriteDenied(string name)
    {
        try
        {
            using var mapping = MemoryMappedFile.OpenExisting("Global\\SkyRoofRemoteBus_" + name,
                MemoryMappedFileRights.ReadWrite);
        }
        catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("Reader could open global mapping for write.");
    }

    private static void VerifyDenied(string name)
    {
        try
        {
            using var mapping = MemoryMappedFile.OpenExisting("Global\\SkyRoofRemoteBus_" + name,
                MemoryMappedFileRights.Read);
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("PASS: unlisted SID cannot open global mapping.");
            return;
        }
        throw new InvalidOperationException("Unlisted SID could read global mapping.");
    }
}
