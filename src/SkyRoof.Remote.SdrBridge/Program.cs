using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.SdrBridge;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This companion uses the packaged Windows SoapySDR runtime.");
if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("CF32 transport is little-endian.");
if (args is ["--abi"])
{
    string abi = SoapyNative.Abi();
    if (abi != "0.8") throw new InvalidOperationException("Unexpected packaged SoapySDR ABI: " + abi);
    Console.WriteLine("PASS: packaged native SoapySDR ABI " + abi);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--list")
{
    SoapyNative.Abi();
    Console.WriteLine(JsonSerializer.Serialize(SoapyRx.Enumerate(args.Length == 2 ? args[1] : "")));
    return;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
try
{
    if (args is [var action, var pipe, var device, var center, var rate, var target, var gain] &&
        action is "--rx" or "--managed-rx")
    {
        if (action == "--managed-rx") _ = StopOnParentExitAsync(stop);
        SoapyNative.Abi();
        using var rx = new SoapyRx(device, Number(center), int.Parse(rate, CultureInfo.InvariantCulture),
            gain == "default" ? null : Number(gain));
        await CaptureAsync(rx, pipe, Number(target) - rx.Frequency, stop.Token);
    }
    else if (args is ["--replay", var file, var filePipe, var fileRate, var offset])
        await ReplayAsync(file, filePipe, int.Parse(fileRate, CultureInfo.InvariantCulture), Number(offset), stop.Token);
    else
    {
        Console.Error.WriteLine("Usage: --list [Soapy args] | --abi | --rx PIPE DEVICE_ARGS CENTER_HZ SAMPLE_RATE CHANNEL_HZ GAIN_DB_OR_default | --replay FILE PIPE SAMPLE_RATE OFFSET_HZ");
        Environment.ExitCode = 2;
    }
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);

static async Task CaptureAsync(SoapyRx rx, string pipe, double offset, CancellationToken ct)
{
    using var sender = new TelemetryIqSender(pipe);
    var converter = new IqChannelizer(rx.SampleRate, offset);
    var buffer = new float[16384];
    Console.WriteLine($"SDR RX actual center={rx.Frequency:R} Hz rate={rx.SampleRate} channel offset={offset:R} Hz, FIR delay={converter.GroupDelaySeconds:R}s");
    while (!ct.IsCancellationRequested)
    {
        int count = rx.Read(buffer);
        if (count == -1 || count == 0) continue;
        if (count == -4)
        {
            await sender.EndAsync();
            converter = new IqChannelizer(rx.SampleRate, offset);
            Console.Error.WriteLine("SDR overflow: closed IQ epoch and reset FIR.");
            continue;
        }
        await sender.SendAsync(converter.Process(buffer.AsSpan(0, count * 2)), ct);
    }
    await sender.EndAsync();
}

static async Task ReplayAsync(string path, string pipe, int rate, double offset, CancellationToken ct)
{
    using var sender = new TelemetryIqSender(pipe);
    var converter = new IqChannelizer(rate, offset);
    await using var input = File.OpenRead(path);
    if (input.Length % 8 != 0) throw new InvalidDataException("CF32 capture length must be divisible by 8.");
    var buffer = new byte[65536];
    while (input.Position < input.Length)
    {
        int count = (int)Math.Min(buffer.Length, input.Length - input.Position);
        await input.ReadExactlyAsync(buffer.AsMemory(0, count), ct);
        await sender.SendAsync(converter.Process(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, count))), ct);
    }
    await sender.EndAsync();
}

static async Task StopOnParentExitAsync(CancellationTokenSource stop)
{
    try { await Console.In.ReadLineAsync(stop.Token); stop.Cancel(); }
    catch (OperationCanceledException) { return; }
    catch (ObjectDisposedException) { return; }
}
