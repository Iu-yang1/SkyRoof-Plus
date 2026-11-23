using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Exercises authenticated Host HTTP diagnostics against a real independent
/// Media named-pipe process, not a mocked HTTP handler or in-process gRPC.
/// </summary>
internal sealed partial class HostMediaStatusProbe : IAsyncDisposable
{
    private readonly Process _host;
    private readonly HttpClient _client;

    private HostMediaStatusProbe(Process host, HttpClient client)
    {
        _host = host;
        _client = client;
    }

    public async Task<JsonElement> WaitForActiveLossAsync(ulong expectedEpoch)
    {
        JsonElement last = default;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            using var response = await _client.GetAsync("/api/v1/media/rx-status");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            last = doc.RootElement.Clone();
            if (last.GetProperty("epoch").GetUInt64() == expectedEpoch &&
                last.GetProperty("streamActive").GetBoolean() &&
                last.GetProperty("datagramsMissing").GetUInt64() == 1)
                return last;
            await Task.Delay(30);
        }
        throw new InvalidOperationException("Host never observed live Media loss: " + last);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        if (!_host.HasExited) _host.Kill(entireProcessTree: true);
        await _host.WaitForExitAsync();
        _host.Dispose();
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
