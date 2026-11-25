using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SkyRoof.Remote.SignalBus;

internal sealed class DspResultHttpProbe : IDisposable
{
    private readonly string _q65Name = "q65-smoke-" + Guid.NewGuid().ToString("N");
    private readonly string _telemetryName = "telemetry-smoke-" + Guid.NewGuid().ToString("N");
    private readonly NamedSignalRing _q65;
    private readonly NamedSignalRing _telemetry;

    public DspResultHttpProbe()
    {
        _q65 = new NamedSignalRing(_q65Name, 32, 32768, 81);
        _telemetry = new NamedSignalRing(_telemetryName, 64, 262144, 82);
        _q65.Publish(0, Stopwatch.GetTimestamp(), 48000, 1, Encoding.UTF8.GetBytes(
            """{"state":"decoded","messages":[{"text":"CQ K1ABC FN42"}]}"""));
        _telemetry.Publish(0, Stopwatch.GetTimestamp(), 48000, 1, Encoding.UTF8.GetBytes(
            """{"state":"frame","hex":"C0DB01","crcValid":true}"""));
    }

    public void Configure(ProcessStartInfo info)
    {
        info.Environment["SKYROOF_Q65_RESULTS_BUS"] = _q65Name;
        info.Environment["SKYROOF_TELEMETRY_RESULTS_BUS"] = _telemetryName;
    }

    public async Task VerifyAsync(HttpClient anonymous, HttpClient authenticated)
    {
        foreach (var (path, epoch, expected) in new[]
        {
            ("/api/v1/dsp/q65", 81UL, "CQ K1ABC FN42"),
            ("/api/v1/dsp/telemetry", 82UL, "C0DB01")
        })
        {
            using var denied = await anonymous.GetAsync(path);
            if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidDataException("Anonymous DSP read allowed.");
            using var deniedConfig = await anonymous.PutAsJsonAsync(path, new { enabled = false });
            if (deniedConfig.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidDataException("Anonymous DSP configuration allowed.");
            using var response = await authenticated.GetAsync(path);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.GetProperty("epoch").GetUInt64() != epoch ||
                !json.RootElement.GetProperty("results").GetRawText().Contains(expected, StringComparison.Ordinal))
                throw new InvalidDataException("DSP route did not write the expected response.");
        }
        Console.WriteLine("PASS: authenticated Q65 and telemetry result HTTP bodies, epochs, anonymous GET/PUT rejection.");
    }

    public void Dispose()
    {
        _q65.Dispose();
        _telemetry.Dispose();
    }
}
