using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class SatnogsHttpFixture : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private const string FixtureToken = "local-ci-not-a-real-token";

    internal SatnogsHttpFixture() => _listener.Start();

    internal void Configure(ProcessStartInfo info)
    {
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        info.Environment["SKYROOF_SATNOGS_TELEMETRY_URL"] = $"http://127.0.0.1:{port}/api/telemetry/";
        info.Environment["SKYROOF_SATNOGS_TOKEN"] = FixtureToken;
        info.Environment["SKYROOF_STATION_CALLSIGN"] = "K1ABC";
        info.Environment["SKYROOF_STATION_GRID"] = "FN42";
    }

    internal async Task VerifyAsync(byte[] decoded, CancellationToken ct)
    {
        using var socket = await _listener.AcceptTcpClientAsync(ct);
        using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
        string? method = await reader.ReadLineAsync(ct);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadLineAsync(ct) is { Length: > 0 } line)
        {
            int colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
        }
        if (method is null || !method.StartsWith("POST /api/telemetry/ ", StringComparison.Ordinal) ||
            headers.GetValueOrDefault("Authorization") != "Token " + FixtureToken ||
            !int.TryParse(headers.GetValueOrDefault("Content-Length"), NumberStyles.None, CultureInfo.InvariantCulture, out int length) ||
            length is <= 0 or > 131072)
            throw new InvalidDataException("Unexpected SatNOGS HTTP request.");
        char[] body = new char[length];
        if (await reader.ReadBlockAsync(body.AsMemory(), ct) != length) throw new EndOfStreamException("Truncated SiDS POST.");
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 201 Created\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
        var fields = new string(body).Split('&').Select(x => x.Split('=', 2))
            .ToDictionary(x => WebUtility.UrlDecode(x[0]), x => WebUtility.UrlDecode(x[1]));
        if (fields.GetValueOrDefault("noradID") != "39444" || fields.GetValueOrDefault("source") != "K1ABC" ||
            fields.GetValueOrDefault("frame") != Convert.ToHexString(decoded) ||
            fields.GetValueOrDefault("latitude") != "42.5000N" || fields.GetValueOrDefault("longitude") != "71.0000W" ||
            !DateTimeOffset.TryParse(fields.GetValueOrDefault("timestamp"), CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("SatNOGS SiDS fields differ from actual decoded frame or station location.");
        Console.WriteLine("PASS: real AO-73 decoded bytes -> authenticated SiDS HTTP POST to isolated local SatNOGS fixture.");
    }

    public void Dispose() => _listener.Stop();
}
