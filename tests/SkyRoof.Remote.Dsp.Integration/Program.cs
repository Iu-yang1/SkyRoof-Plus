using System.Buffers.Binary;
using System.Diagnostics;
using SkyRoof.Remote.SignalBus;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
var dll = Path.GetFullPath(args.Single());
var suffix = Guid.NewGuid().ToString("N");
var rxName = "skyroof-dsp-rx-" + suffix;
var spectrumName = "skyroof-dsp-af-" + suffix;
var ft4Name = "skyroof-dsp-ft4-" + suffix;
var cwName = "skyroof-dsp-cw-" + suffix;
var q65Name = "skyroof-dsp-q65-" + suffix;
var telemetryName = "skyroof-dsp-tlm-" + suffix;
var dspPipe = "skyroof-dsp-pipe-" + suffix;
var history = Path.Combine(Path.GetTempPath(), "skyroof-cw-history-" + suffix);
using var rx = new NamedSignalRing(rxName, 64, 4096, initialEpoch: 11);
var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
info.ArgumentList.Add(dll);
info.Environment["SKYROOF_RX_AUDIO_BUS"] = rxName;
info.Environment["SKYROOF_AF_SPECTRUM_BUS"] = spectrumName;
info.Environment["SKYROOF_RX_AUDIO_CHANNELS"] = "1";
info.Environment["SKYROOF_FT4_RESULTS_BUS"] = ft4Name;
info.Environment["SKYROOF_FT4_RX_DELAY_MS"] = "0";
info.Environment["SKYROOF_DSP_PIPE"] = dspPipe;
info.Environment["SKYROOF_CW_RESULTS_BUS"] = cwName;
info.Environment["SKYROOF_Q65_RESULTS_BUS"] = q65Name;
info.Environment["SKYROOF_TELEMETRY_RESULTS_BUS"] = telemetryName;
info.Environment["SKYROOF_CW_HISTORY_DIRECTORY"] = history;
var archive = Path.Combine(history, "telemetry");
info.Environment["SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY"] = archive;
info.Environment["SKYROOF_TELEMETRY_KISS_BIND"] = "127.0.0.1";
using var satnogs = new SatnogsHttpFixture();
satnogs.Configure(info);
using var process = Process.Start(info) ?? throw new InvalidOperationException("DSP process not started");
try
{
    NamedSignalRing? opened = null;
    for (var attempt = 0; attempt < 100; ++attempt)
    {
        try { opened = new NamedSignalRing(spectrumName, 64, 4096); break; }
        catch (InvalidDataException) { await Task.Delay(50); }
    }
    using var spectrum = opened ?? throw new TimeoutException("DSP spectrum bus unavailable");
    var cursor = new SignalCursor();

    static byte[] Tone(int start, int count, double hz)
    {
        var pcm = new byte[count * 2];
        for (var i = 0; i < count; ++i)
        {
            var sample = (short)Math.Round(16384 * Math.Sin(2 * Math.PI * hz * (start + i) / 48000));
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), sample);
        }
        return pcm;
    }
    async Task FeedAsync(double hz)
    {
        for (var n = 0; n < 32; ++n)
        {
            var start = n * 480;
            rx.Publish((ulong)start, Stopwatch.GetTimestamp(), 48000, 480, Tone(start, 480, hz));
            await Task.Delay(20);
        }
    }

    static (double Frequency, float Level) Peak(SignalFrame frame)
    {
        if (frame.SampleRate != 48000 || frame.SampleCount != 475 || frame.Payload.Length != 1900)
            throw new InvalidDataException("Wrong AF spectrum output geometry.");
        var max = float.MinValue;
        var peak = -1;
        for (var i = 1; i < 475; ++i)
        {
            var power = BinaryPrimitives.ReadSingleLittleEndian(frame.Payload.AsSpan(i * 4, 4));
            if (!float.IsFinite(power)) throw new InvalidDataException("Nonfinite AF FFT output.");
            if (power > max) { max = power; peak = i; }
        }
        return (peak * 24000.0 / 474, max);
    }

    await FeedAsync(1000);
    SignalFrame? first = null;
    var limit = Stopwatch.StartNew();
    while (limit.Elapsed < TimeSpan.FromSeconds(8))
    {
        if (spectrum.TryReadNext(cursor, out var candidate) && candidate is not null)
            first = candidate;
        if (first is not null) break;
        await Task.Delay(40);
    }
    if (first is null) throw new TimeoutException("DSP emitted no spectrum frames.");
    var before = Peak(first);
    Check(Math.Abs(before.Frequency - 1000) < 80 && before.Level is > -15 and < -2,
        $"Expected 1 kHz tone around -6 dBFS; got {before.Frequency:F1} Hz {before.Level:F1} dBFS.");
    var epochBefore = first.StreamEpoch;

    rx.ResetEpoch(12);
    await FeedAsync(2500);
    SignalFrame? second = null;
    limit.Restart();
    while (limit.Elapsed < TimeSpan.FromSeconds(8))
    {
        while (spectrum.TryReadNext(cursor, out var candidate) && candidate is not null)
            if (candidate.StreamEpoch != epochBefore) second = candidate;
        if (second is not null) break;
        await Task.Delay(40);
    }
    if (second is null) throw new TimeoutException("DSP did not reset the outgoing epoch.");
    var after = Peak(second);
    Check(Math.Abs(after.Frequency - 2500) < 80 && after.Level is > -15 and < -2,
        $"Expected post-restart 2.5 kHz tone; got {after.Frequency:F1} Hz {after.Level:F1} dBFS.");
    Console.WriteLine($"PASS: DSP true AF FFT 1000 Hz -> {before.Frequency:F1} Hz, 2500 Hz -> {after.Frequency:F1} Hz; independent epochs and dBFS.");
    await Ft4LiveProbe.RunAsync(dll, rx, ft4Name);
    await CwLiveProbe.RunAsync(rx, cwName, dspPipe, history);
    if (File.Exists(Path.Combine(Path.GetDirectoryName(dll)!, "mfsk.dll")))
        await Q65LiveProbe.RunAsync(rx, q65Name, dspPipe);
    if (Environment.GetEnvironmentVariable("SKYROOF_SATELLITE_AUDIO_CORPUS") is { Length: > 0 } corpus)
        await TelemetryLiveProbe.RunAsync(rx, telemetryName, dspPipe, corpus, archive, satnogs);
}
finally
{
    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
    if (Directory.Exists(history)) Directory.Delete(history, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
