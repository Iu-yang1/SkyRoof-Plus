using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

internal sealed record NativeFt4ProcessPaths(string Host, string Station, string Dsp);

internal static partial class NativeFt4HttpProbe
{
    public static async Task RunAsync(NativeFt4ProcessPaths paths, string devicePipe,
        string suffix, FakeNativeIcomRadio fake, CancellationToken ct)
    {
        string stationPipe = "skyroof-ft4-station-" + suffix;
        string dspPipe = "skyroof-ft4-dsp-" + suffix;
        string root = Path.Combine(Path.GetTempPath(), "skyroof-ft4-e2e-" + suffix);
        Directory.CreateDirectory(root);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
        listener.Stop();
        var processes = new List<Process>();
        try
        {
            StartProcesses(paths, new Fixture(devicePipe, stationPipe, dspPipe, root, address), processes);
            using var anonymous = new HttpClient { BaseAddress = address };
            await WaitReadyAsync(anonymous, ct);
            await VerifyHttpAsync(anonymous, address, dspPipe, fake,
                File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(paths.Dsp)) ?? "", "mfsk.dll")), ct);
        }
        finally
        {
            foreach (var process in processes)
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            foreach (var process in processes)
            {
                await process.WaitForExitAsync();
                process.Dispose();
            }
            await DeleteFixtureAsync(root);
        }
    }

    private static async Task DeleteFixtureAsync(string root)
    {
        // Windows can briefly retain a SQLite handle after the Host process exits.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (IOException ex) when (attempt < 20 && (ex.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(100);
            }
        }
    }

    private static async Task WaitReadyAsync(HttpClient client, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            try
            {
                using var response = await client.GetAsync("/health/ready", timeout.Token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { Console.WriteLine("Waiting for FT4 E2E Host."); }
            await Task.Delay(100, timeout.Token);
        }
    }

    private sealed record Fixture(string DevicePipe, string StationPipe, string DspPipe, string Root, Uri Address);
}
