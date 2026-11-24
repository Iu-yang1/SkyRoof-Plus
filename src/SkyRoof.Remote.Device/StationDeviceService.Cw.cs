using Grpc.Core;
using SkyRoof.CW;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    private sealed class ActiveCw(ICwKeyerSession keyer, string sessionId, TimeSpan duration)
    {
        public ICwKeyerSession Keyer { get; } = keyer;
        public string SessionId { get; } = sessionId;
        public TimeSpan Duration { get; } = duration;
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private ActiveCw? _cwActive;

    private static int CwPort => int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_CW_KEYER_PORT"),
        out int port) ? port : 4538;

    private async Task<ICwKeyerSession> ConnectCwAsync(CancellationToken ct)
    {
        if (cat is not RadioCatBackend { SupportsCwKeyer: true })
            throw new InvalidOperationException("CW keyer requires the SkyCAT CAT backend.");
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

    private async Task<ActiveCw> StartCwAsync(string message, string sessionId, CancellationToken ct)
    {
        await _commands.WaitAsync(ct);
        try
        {
            await RecoverOrphanedPttAsync(ct);
            if (_owner is not null || _cwActive is not null)
                throw new InvalidOperationException("Transmitter is owned by another transaction.");
            var keyer = await ConnectCwAsync(ct);
            try
            {
                var status = await keyer.GetStatusAsync(ct);
                if (!status.ReadyToSend) throw new InvalidOperationException(
                    $"CW not ready: {status.Lease}, {status.Mode}, BK-IN {status.BreakIn}, TX {status.Transmitting}.");
                await keyer.SendAsync(message, ct);
                var active = new ActiveCw(keyer, sessionId, CwMessageTiming.ComputeWatchdog(message, status.KeySpeedRaw));
                _cwActive = active;
                return active;
            }
            catch { await keyer.DisposeAsync(); throw; }
        }
        finally { _commands.Release(); }
    }

    private async Task StopCwLockedAsync(ActiveCw active)
    {
        if (!ReferenceEquals(_cwActive, active)) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try { await active.Keyer.StopAsync(timeout.Token); }
        finally
        {
            try { await active.Keyer.DisposeAsync(); }
            finally
            {
                _cwActive = null;
                active.Stopped.TrySetResult();
            }
        }
    }
}
