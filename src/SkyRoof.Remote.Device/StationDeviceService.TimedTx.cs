using System.Security.Cryptography;
using Grpc.Core;
using SkyRoof.Remote.Foundation.Timing;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    private string? _timedTransaction;
    private static readonly TimeSpan KeyLead = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(100);
    private const long MaxLatenessNs = 80_000_000;

    public override async Task<TimedTxReceipt> RunTimedTx(TimedTxRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        try
        {
            var prepared = await PrepareTimedTxAsync(request, ct);
            return await ExecuteTimedTxAsync(prepared, ct);
        }
        catch (ArgumentException e) { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
        catch (InvalidOperationException e) { throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message)); }
        catch (Exception e) when (e is IOException or TimeoutException or System.Net.Sockets.SocketException)
        { throw FailedCat(e); }
    }

    private async Task<PreparedTimedTx> PrepareTimedTxAsync(TimedTxRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.JobId, out Guid id) || id == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.AuthorizedPrincipal) ||
            request.FrequencyHz <= 0 || string.IsNullOrWhiteSpace(request.Mode) ||
            request.Pcm16.Length < 2 || request.Pcm16.Length % 2 != 0)
            throw new ArgumentException("Invalid timed TX metadata or PCM.");
        byte[] bytes = request.Pcm16.ToByteArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!hash.Equals(request.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Timed TX SHA-256 mismatch.");
        var clock = StationMonotonicClock.Capture();
        var scheduler = new TimedTxScheduler();
        scheduler.Seal(id, request.AuthorizedPrincipal, request.TargetUtcNanoseconds,
            48000, bytes.Length / 2, hash);
        var job = scheduler.Arm(id, clock, StationMonotonicClock.Now, (KeyLead + TimeSpan.FromMilliseconds(200)).Ticks * 100);
        var native = cat as NativeIcomCatBackend;
        var audio = native?.Tx ?? throw new InvalidOperationException("Timed TX requires an active native TX Audio Grant.");
        var waveform = audio.PrepareWaveform(bytes);
        await SealOnceAsync(id, bytes, ct);
        var command = new DeviceCommand { Operation = DeviceOperation.DeviceDigitalBegin,
            TransactionId = id.ToString("N"), SessionId = request.SessionId,
            FrequencyHz = request.FrequencyHz, Mode = request.Mode, AudioSource = "LAN" };
        return new(job, scheduler, command, audio, waveform);
    }

    private static async Task SealOnceAsync(Guid id, byte[] pcm, CancellationToken ct)
    {
        string root = Environment.GetEnvironmentVariable("SKYROOF_TIMED_TX_DIRECTORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SkyRoof", "Remote", "timed-tx");
        Directory.CreateDirectory(root);
        // Durable single-use claim: never automatically replay a possibly keyed job.
        await using (var claim = new FileStream(Path.Combine(root, id.ToString("N") + ".claim"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await claim.WriteAsync(id.ToByteArray(), ct);
            claim.Flush(flushToDisk: true);
        }
        var store = new TimedWaveformStore(root);
        using var input = new MemoryStream(pcm, writable: false);
        await store.UploadAndSealAsync(new(id, 48000, 1, TimedWaveformFormat.Pcm16Le, pcm.Length / 2), input, ct);
        if (!await store.VerifyAsync(id, ct)) throw new InvalidDataException("Sealed timed waveform verification failed.");
    }

    private sealed record PreparedTimedTx(TimedTxJob Job, TimedTxScheduler Scheduler,
        DeviceCommand Command, NativeIcomTxAudio Audio, TimedNativeWaveform Waveform);
}
