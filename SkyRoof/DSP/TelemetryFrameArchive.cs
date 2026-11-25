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
    private abstract record ArchiveItem;
    private sealed record Entry(
      byte[] Bytes, DateTime Utc, long Sequence, int? NoradId,
      string Satellite, string Transmitter, double CfoHz, double SnrDb,
      bool? CrcValid, int CorrectedBits, int ErasedBytes,
      string Framing, string Note, bool InnerTransport) : ArchiveItem;
    private sealed record TextEntry(DateTime Utc, string Text) : ArchiveItem;
    private sealed record Barrier(TaskCompletionSource Completion) : ArchiveItem;
#if SKYROOF_REMOTE_DSP
    private readonly string session = Guid.NewGuid().ToString("N")[..8];
#endif

    private readonly Channel<ArchiveItem> queue = Channel.CreateUnbounded<ArchiveItem>(
      new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task worker;
    private readonly string rootDirectory;
    private long sequence;
    private long saved;
    private long errors;

    internal long Saved => Interlocked.Read(ref saved);
    internal long Errors => Interlocked.Read(ref errors);

    internal TelemetryFrameArchive(string? rootDirectory = null)
    {
#if SKYROOF_REMOTE_DSP
      this.rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
#else
      this.rootDirectory = rootDirectory ?? Path.Combine(Utils.GetUserDataFolder(),
        "TelemetryDecodes", "Binary");
#endif
      worker = Task.Run(ProcessAsync);
    }

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

    // The readable legacy frames_YYYYMMDD.txt is written through the
    // very same background queue as the binary records. No disk I/O on
    // WinForms or on the decoder callback thread.
    internal void OfferText(string text, DateTime utc) =>
      queue.Writer.TryWrite(new TextEntry(utc, text));

    internal async Task FlushAsync()
    {
      var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      await queue.Writer.WriteAsync(new Barrier(completion)).ConfigureAwait(false);
      await completion.Task.ConfigureAwait(false);
    }

    internal async Task CompleteAsync()
    {
      queue.Writer.TryComplete();
      await worker.ConfigureAwait(false);
    }

    private async Task ProcessAsync()
    {
      await foreach (var item in queue.Reader.ReadAllAsync().ConfigureAwait(false))
      {
        if (item is Barrier barrier)
        {
          barrier.Completion.TrySetResult();
          continue;
        }
        try
        {
          if (item is TextEntry text)
          {
            Directory.CreateDirectory(rootDirectory);
            await File.AppendAllTextAsync(
              Path.Combine(rootDirectory,
                $"frames_{text.Utc:yyyyMMdd}.txt"),
              text.Text + Environment.NewLine, new UTF8Encoding(false))
              .ConfigureAwait(false);
            continue;
          }
          var entry = (Entry)item;
          string root = Path.Combine(rootDirectory,
            (entry.NoradId?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"),
            entry.Utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
          Directory.CreateDirectory(root);
          string stem = entry.Utc.ToString("yyyyMMdd_HHmmss_fff",
            CultureInfo.InvariantCulture) +
            "_" + entry.Sequence.ToString("D8", CultureInfo.InvariantCulture);
#if SKYROOF_REMOTE_DSP
          stem += "_" + session;
#endif
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
