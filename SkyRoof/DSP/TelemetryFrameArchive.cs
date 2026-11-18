using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Serilog;
using VE3NEA;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof
{
  /// <summary>
  /// Serial background archiver for decoded telemetry payloads. Each .bin is
  /// exactly Frame.Bytes (after deframing/descrambling, CRC removed where the
  /// protocol strips it); the matching .log records provenance and integrity.
  /// This deliberately does not claim to save raw on-air bits or undecodable
  /// bursts. The separate legacy daily text log is unchanged.
  /// </summary>
  internal sealed class TelemetryFrameArchive
  {
    private sealed record Entry(
      byte[] Bytes, DateTime Utc, long Sequence, int? NoradId,
      string Satellite, string Transmitter, double CfoHz, double SnrDb,
      bool? CrcValid, int CorrectedBits, int ErasedBytes,
      string Framing, string Note, bool InnerTransport);

    private readonly Channel<Entry> queue = Channel.CreateUnbounded<Entry>(
      new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task worker;
    private long sequence;
    private long saved;
    private long errors;

    internal long Saved => Interlocked.Read(ref saved);
    internal long Errors => Interlocked.Read(ref errors);

    internal TelemetryFrameArchive() => worker = Task.Run(ProcessAsync);

    internal void Offer(Frame frame, int? noradId, string? satellite,
      string? transmitter, bool innerTransport)
    {
      var entry = new Entry(
        (byte[])frame.Bytes.Clone(), DateTime.UtcNow,
        Interlocked.Increment(ref sequence), noradId,
        satellite ?? "Unknown", transmitter ?? "Unknown",
        frame.CfoHz, frame.SnrDb, frame.CrcValid,
        frame.CorrectedBits, frame.ErasedBytes,
        frame.Framing.ToString(), frame.Note ?? "", innerTransport);
      queue.Writer.TryWrite(entry);
    }

    internal async Task CompleteAsync()
    {
      queue.Writer.TryComplete();
      await worker.ConfigureAwait(false);
    }

    private async Task ProcessAsync()
    {
      await foreach (var entry in queue.Reader.ReadAllAsync().ConfigureAwait(false))
      {
        try
        {
          string root = Path.Combine(Utils.GetUserDataFolder(),
            "TelemetryDecodes", "Binary",
            (entry.NoradId?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"),
            entry.Utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
          Directory.CreateDirectory(root);
          string stem = entry.Utc.ToString("yyyyMMdd_HHmmss_fff",
            CultureInfo.InvariantCulture) +
            "_" + entry.Sequence.ToString("D8", CultureInfo.InvariantCulture);
          string bin = Path.Combine(root, stem + ".bin");
          string log = Path.Combine(root, stem + ".log");
          // One file per packet: tools can load the raw decoded payload without
          // a proprietary container header, even when packet lengths differ.
          await File.WriteAllBytesAsync(bin, entry.Bytes).ConfigureAwait(false);
          string metadata =
            $"UTC: {entry.Utc:yyyy-MM-ddTHH:mm:ss.fff}Z\n" +
            $"NORAD: {entry.NoradId?.ToString() ?? "unknown"}\n" +
            $"Satellite: {entry.Satellite}\n" +
            $"Transmitter: {entry.Transmitter}\n" +
            $"Type: {(entry.InnerTransport ? "inner transport payload" : "outer decoded payload")}\n" +
            $"Bytes: {entry.Bytes.Length}\n" +
            $"Framing: {entry.Framing}\n" +
            $"CRC/FEC: {entry.CrcValid switch { true => "OK", false => "FAIL", null => "n/a" }}\n" +
            $"Corrections: {entry.CorrectedBits}\n" +
            $"Erasures: {entry.ErasedBytes}\n" +
            $"CFO: {entry.CfoHz:F1} Hz\n" +
            $"SNR: {entry.SnrDb:F1} dB\n" +
            $"Note: {entry.Note}\n" +
            $"Binary: {Path.GetFileName(bin)}\n" +
            $"HEX: {Convert.ToHexString(entry.Bytes)}\n";
          await File.WriteAllTextAsync(log, metadata, new UTF8Encoding(false))
            .ConfigureAwait(false);
          Interlocked.Increment(ref saved);
        }
        catch (Exception e)
        {
          Interlocked.Increment(ref errors);
          Log.Warning(e, "Telemetry BIN+LOG archive write failed");
        }
      }
    }
  }
}
