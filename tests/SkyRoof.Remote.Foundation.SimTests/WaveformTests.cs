using SkyRoof.Remote.Foundation.Timing;

internal static class WaveformTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyroof-waveform-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TimedWaveformStore(root);
            var id = Guid.NewGuid();
            var request = new WaveformUploadRequest(id, 48000, 1,
                TimedWaveformFormat.Pcm16Le, 480);
            var bytes = Enumerable.Range(0, 960).Select(x => (byte)(x % 251)).ToArray();
            var result = store.UploadAndSealAsync(request, new MemoryStream(bytes)).GetAwaiter().GetResult();
            Check(result.PayloadBytes == 960 && result.Sha256.Length == 64);
            Check(store.VerifyAsync(id).GetAwaiter().GetResult());
            var restored = new TimedWaveformStore(root);
            Check(restored.ReadSealedAsync(id).GetAwaiter().GetResult() == result);
            Check(restored.UploadAndSealAsync(request, new MemoryStream(bytes)).GetAwaiter().GetResult() == result);
            ExpectInvalid(() => restored.UploadAndSealAsync(request, new MemoryStream(new byte[960])).GetAwaiter().GetResult());
            ExpectInvalidData(() => restored.UploadAndSealAsync(request with { JobId = Guid.NewGuid() },
                new MemoryStream(new byte[959])).GetAwaiter().GetResult());
            File.WriteAllBytes(Path.Combine(root, result.FileName), new byte[960]);
            Check(!restored.VerifyAsync(id).GetAwaiter().GetResult());
            Console.WriteLine("PASS: staged waveform integrity, idempotence, restart, tampering and size checks.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected a duplicate-job rejection.");
    }
    private static void ExpectInvalidData(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected a waveform size rejection.");
    }
    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Waveform simulation failed.");
    }
}
