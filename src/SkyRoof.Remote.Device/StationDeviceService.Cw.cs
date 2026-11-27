using Grpc.Core;
using SkyRoof.CW;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    private sealed class ActiveCw(ICwKeyerSession keyer, string sessionId, string transactionId, TimeSpan duration)
    {
        public ICwKeyerSession Keyer { get; } = keyer;
        public string SessionId { get; } = sessionId;
        public string TransactionId { get; } = transactionId;
        public TimeSpan Duration { get; } = duration;
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private ActiveCw? _cwActive;

    private static int CwPort => int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_CW_KEYER_PORT"),
        out int port) ? port : 4538;

    private async Task<ICwKeyerSession> ConnectCwAsync(CancellationToken ct)
    {
        if (cat is NativeIcomCatBackend native) return native.CreateCwKeyer();
        if (cat is not RadioCatBackend { SupportsCwKeyer: true })
            throw new InvalidOperationException("CW keyer requires SkyCAT or native IC-9700 LAN.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        return await new CwKeyerTcpSessionFactory().ConnectAsync(CwPort, timeout.Token);
    }

    public override async Task<CwSendReceipt> SendCw(CwSendRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        ActiveCw? active = null;
        try
        {
            string message = request.Text.Trim().ToUpperInvariant();
            CwMessageTiming.ValidateText(message);
            if (string.IsNullOrWhiteSpace(request.SessionId)) throw new ArgumentException("Missing CW session.");
            active = await StartCwAsync(message, request.SessionId, ct);
            var delay = Task.Delay(active.Duration, ct);
            var finished = await Task.WhenAny(delay, active.Stopped.Task);
            if (finished == delay) await delay;
            return new CwSendReceipt { Text = message,
                State = finished == delay ? "watchdog-elapsed" : "interrupted" };
        }
        catch (ArgumentException e) { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
        catch (InvalidOperationException e) { throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message)); }
        catch (Exception e) when (e is IOException or TimeoutException or FormatException or System.Net.Sockets.SocketException)
        { throw FailedCat(e); }
        finally
        {
            if (active is not null)
            {
                await _commands.WaitAsync();
                try { await StopCwLockedAsync(active); }
                finally { _commands.Release(); }
            }
        }
    }

}
