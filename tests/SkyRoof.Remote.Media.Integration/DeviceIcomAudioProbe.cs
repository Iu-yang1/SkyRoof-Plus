using System.Diagnostics;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.SignalBus;

internal static partial class DeviceIcomAudioProbe
{
    public static async Task RunAsync(string deviceDll, string mediaPipe,
        string suffix, NamedSignalRing ring, MediaIngress.MediaIngressClient media, NativeFt4ProcessPaths? ft4Paths)
    {
        using var fake = new FakeNativeIcomRadio();
        fake.SeedOrphanedCw();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ft4Paths is null ? 90 : 240));
        var radioTask = fake.RunAsync(deadline.Token);
        var startEpoch = ring.StreamEpoch;
        string journal = Path.Combine(Path.GetTempPath(), "skyroof-native-icom-" + suffix + ".json");
        string timedDir = Path.Combine(Path.GetTempPath(), "skyroof-timed-" + suffix);
        var info = CreateDeviceProcess(deviceDll, mediaPipe, suffix, journal, timedDir, fake);
        using var process = Process.Start(info) ??
            throw new InvalidOperationException("Icom Device worker did not start.");

        try
        {
            await NativeDeviceCatProbe.RunAsync(info.Environment["SKYROOF_DEVICE_PIPE"]
                ?? throw new InvalidOperationException("Device test pipe missing."), fake, deadline.Token);
            if (File.Exists(journal)) throw new InvalidDataException("Native CW journal survived recovery and release.");
            if (ft4Paths is not null)
                await NativeFt4HttpProbe.RunAsync(ft4Paths,
                    info.Environment["SKYROOF_DEVICE_PIPE"] ?? throw new InvalidOperationException("Missing Device pipe."),
                    suffix, fake, deadline.Token);
            fake.FinishNativeCat();
            var observed = new Dictionary<ulong, byte>();
            var cursor = new SignalCursor();
            for (int attempt = 0; attempt < 150 && observed.Count < 4; attempt++)
            {
                while (ring.TryReadNext(cursor, out var frame) && frame is not null)
                {
                    if (frame.StreamEpoch == startEpoch || frame.SampleCount != 480)
                        continue;
                    if (!frame.Payload.All(value => value == frame.Payload[0]))
                        throw new InvalidOperationException("Raw UDP PCM was altered by Device->Media.");
                    observed[frame.FirstSampleIndex] = frame.Payload[0];
                }
                if (observed.Count < 4) await Task.Delay(30);
            }
            if (observed.Count != 4 ||
                !observed.TryGetValue(0, out var a) || a != 0x11 ||
                !observed.TryGetValue(480, out var b) || b != 0x22 ||
                !observed.TryGetValue(960, out var c) || c != 0x33 ||
                !observed.TryGetValue(1920, out var d) || d != 0x44)
                throw new InvalidOperationException(
                    "Device did not deliver authentic UDP audio in source order across Media IPC.");

            var receipt = await media.GetRxStatusAsync(new RxStatusQuery());
            if (receipt.Source != "icom-lan" || !receipt.StreamActive ||
                receipt.FramesWritten < 4 || receipt.SamplesSkipped != 480 ||
                receipt.DatagramsMissing != 1)
                throw new InvalidOperationException(
                    "Raw UDP Device/Media loss and epoch diagnostics did not agree.");
            Console.WriteLine("PASS: real UDP fake IC-9700 login/grant/CI-V/audio -> Device IPC -> Media SignalBus.");
            await fake.DisconnectControlAsync(deadline.Token);
            await AssertSourceEndedAsync(media, receipt.Epoch, deadline.Token);
            await radioTask;
            if (ft4Paths is not null && fake.RenewalCount == 0)
                throw new InvalidDataException("Packaged FT4 did not exercise control token renewal.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            if (File.Exists(journal)) File.Delete(journal);
            if (Directory.Exists(timedDir)) Directory.Delete(timedDir, recursive: true);
        }
    }
    private static async Task AssertSourceEndedAsync(
        MediaIngress.MediaIngressClient media, ulong epoch, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var status = await media.GetRxStatusAsync(new RxStatusQuery(), cancellationToken: ct);
            if (!status.StreamActive && status.Epoch == epoch)
            {
                Console.WriteLine("PASS: native control disconnect ends the old Media RX epoch.");
                return;
            }
            await Task.Delay(40, ct);
        }
        throw new InvalidOperationException("Control disconnect left the old Media source active.");
    }
}
