using System.Buffers.Binary;
using System.Security.Cryptography;
using SkyRoof.Remote.Dsp;
using SkyRoof.Remote.SignalBus;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("DSP SignalBus uses Windows shared memory.");
if (args is ["--q65-self-test"])
{
    Q65Command.SelfTest();
    return;
}
if (args is ["--ft4-self-test"])
{
    Ft4Command.SelfTest();
    return;
}
if (args is ["--ft4-encode", var message, var output])
{
    Ft4Command.EncodeFile(message, output);
    return;
}
if (args is ["--ft4-decode", var pcmPath])
{
    Ft4Command.DecodeFile(pcmPath);
    return;
}
var inputName = Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_BUS") ?? "skyroof-rx-audio";
var outputName = Environment.GetEnvironmentVariable("SKYROOF_AF_SPECTRUM_BUS") ?? "skyroof-af-spectrum";
var channels = int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_RX_AUDIO_CHANNELS"),
    out var requested) ? requested : 1;
if (channels is < 1 or > 2) throw new ArgumentException("Invalid RX channel count.");

await using var cw = new CwLiveReceiver(channels);
await using var ipc = await DspIpcHost.StartAsync(cw);
using var spectrum = new NamedSignalRing(outputName, slots: 64, payloadCapacity: 4096,
    initialEpoch: (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue));
var analyzer = new AudioSpectrumTransform();
await using var ft4 = new Ft4LiveReceiver(channels);
ulong inputEpoch = 0;
Console.WriteLine($"SkyRoof DSP: input={inputName}, AF spectrum={outputName}, channels={channels}");

while (true)
{
    try
    {
        using var input = new NamedSignalRing(inputName, slots: 64, payloadCapacity: 4096);
        var cursor = new SignalCursor();
        while (true)
        {
            if (!input.TryReadNext(cursor, out var frame) || frame is null)
            {
                await Task.Delay(10);
                continue;
            }
            if (inputEpoch != frame.StreamEpoch)
            {
                inputEpoch = frame.StreamEpoch;
                analyzer.Reset(inputEpoch);
                spectrum.ResetEpoch((ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue));
            }
            ft4.Accept(frame);
            cw.Accept(frame);
            var oldDiscontinuities = analyzer.Discontinuities;
            analyzer.Accept(frame, channels, (firstSample, db) =>
                {
                    var payload = new byte[db.Length * sizeof(float)];
                    for (var i = 0; i < db.Length; ++i)
                        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(i * 4, 4), db[i]);
                    spectrum.Publish(firstSample, frame.MonotonicTimestampTicks, frame.SampleRate,
                        db.Length, payload);
                });
            if (analyzer.Discontinuities != oldDiscontinuities)
                spectrum.ResetEpoch((ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue));
        }
    }
    catch (InvalidDataException)
    {
        // Media may not have created the RX ring yet.
        await Task.Delay(200);
    }
}
