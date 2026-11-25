using System.Net;
using System.Net.Sockets;

internal static partial class TelemetryLiveProbe
{
    private static int ReserveKissPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<byte[]> ReadKissAsync(NetworkStream stream, CancellationToken ct)
    {
        var chunk = new byte[1024];
        var bytes = new List<byte>();
        bool started = false, escaped = false;
        while (true)
        {
            int count = await stream.ReadAsync(chunk, ct);
            if (count == 0) throw new EndOfStreamException("KISS connection ended before a complete frame.");
            foreach (byte value in chunk.AsSpan(0, count))
            {
                if (value == 0xc0)
                {
                    if (bytes.Count > 0)
                    {
                        if (escaped || bytes[0] != 0) throw new InvalidDataException("Unexpected RF KISS virtual channel.");
                        return bytes.Skip(1).ToArray();
                    }
                    started = true;
                    continue;
                }
                if (!started) continue;
                if (escaped)
                {
                    bytes.Add(value switch { 0xdc => (byte)0xc0, 0xdd => (byte)0xdb,
                        _ => throw new InvalidDataException("Invalid KISS escape.") });
                    escaped = false;
                }
                else if (value == 0xdb) escaped = true;
                else bytes.Add(value);
            }
        }
    }

    private static void VerifyOutputs(byte[] decoded, byte[] kiss, string archive)
    {
        if (!decoded.AsSpan().SequenceEqual(kiss)) throw new InvalidDataException("KISS bytes differ from decoded frame.");
        string[] files = Directory.GetFiles(archive, "*.bin", SearchOption.AllDirectories);
        if (files.Length != 1 || !File.ReadAllBytes(files[0]).AsSpan().SequenceEqual(decoded))
            throw new InvalidDataException("BIN archive differs from verified decoded bytes.");
        string log = File.ReadAllText(Path.ChangeExtension(files[0], ".log"));
        if (!log.Contains("CRC/FEC: OK", StringComparison.Ordinal) ||
            !log.Contains("NORAD: 39444", StringComparison.Ordinal) ||
            !log.Contains(Convert.ToHexString(decoded), StringComparison.Ordinal))
            throw new InvalidDataException("Archive LOG metadata differs from frame.");
    }
}
