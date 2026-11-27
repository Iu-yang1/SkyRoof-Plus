using System.Globalization;
using System.Net;
using System.Threading.Channels;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Remote.Dsp;

internal sealed partial class RemoteSatnogsUploader : IAsyncDisposable
{
    private sealed record Submission(int Norad, string Hex, DateTimeOffset Utc);
    private readonly Channel<Submission> _queue = Channel.CreateBounded<Submission>(new BoundedChannelOptions(128)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _http;
    private readonly Task _worker;
    private readonly string _token, _call, _grid;
    private readonly Uri _endpoint;
    private long _sent, _failed, _dropped;
    private string _last = "waiting";

    public RemoteSatnogsUploader()
    {
        _token = Environment.GetEnvironmentVariable("SKYROOF_SATNOGS_TOKEN")?.Trim() ?? "";
        _call = Environment.GetEnvironmentVariable("SKYROOF_STATION_CALLSIGN")?.Trim().ToUpperInvariant() ?? "";
        _grid = Environment.GetEnvironmentVariable("SKYROOF_STATION_GRID")?.Trim().ToUpperInvariant() ?? "";
        _endpoint = new Uri(Environment.GetEnvironmentVariable("SKYROOF_SATNOGS_TELEMETRY_URL") ??
            "https://db.satnogs.org/api/telemetry/");
        if (_endpoint.Scheme != "https" && !(_endpoint.Scheme == "http" && _endpoint.IsLoopback))
            throw new ArgumentException("SatNOGS requires HTTPS or a local test endpoint.");
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _worker = Task.Run(RunAsync);
    }

    public bool Ready => _token.Length > 0 && _call.Length > 0 && TryGrid(_grid, out _, out _);
    public object Status => new { ready = Ready, sent = Interlocked.Read(ref _sent), failed = Interlocked.Read(ref _failed),
        dropped = Interlocked.Read(ref _dropped), last = Volatile.Read(ref _last) };

    public void Offer(Frame frame, int norad, bool inner)
    {
        if (inner || frame.CrcValid == false || norad <= 0) return;
        if (!_queue.Writer.TryWrite(new Submission(norad, Convert.ToHexString(frame.Bytes), DateTimeOffset.UtcNow)))
            Interlocked.Increment(ref _dropped);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                try
                {
                    using var request = MakeRequest(item);
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _stop.Token);
                    if (response.IsSuccessStatusCode) Interlocked.Increment(ref _sent);
                    else Interlocked.Increment(ref _failed);
                    Volatile.Write(ref _last, "HTTP " + (int)response.StatusCode);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
                {
                    Interlocked.Increment(ref _failed);
                    Volatile.Write(ref _last, error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private HttpRequestMessage MakeRequest(Submission item)
    {
        if (!TryGrid(_grid, out double lat, out double lon)) throw new InvalidOperationException("Station grid invalid.");
        var fields = new Dictionary<string, string>
        {
            ["noradID"] = item.Norad.ToString(CultureInfo.InvariantCulture), ["source"] = _call,
            ["locator"] = "longLat", ["longitude"] = Coordinate(lon, "E", "W"), ["latitude"] = Coordinate(lat, "N", "S"),
            ["timestamp"] = item.Utc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["frame"] = item.Hex, ["version"] = "SkyRoof Remote 1.7"
        };
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.TryAddWithoutValidation("Authorization", "Token " + _token);
        return request;
    }

    private static string Coordinate(double value, string positive, string negative) =>
        Math.Abs(value).ToString("F4", CultureInfo.InvariantCulture) + (value >= 0 ? positive : negative);

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _stop.CancelAsync();
        try { await _worker; }
        finally { _http.Dispose(); _stop.Dispose(); }
    }
}
