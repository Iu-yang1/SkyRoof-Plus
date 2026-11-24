using Grpc.Core;
using SkyRoof.CW;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed partial class StationDeviceService
{
    public override Task<CwRadioStatus> GetCwStatus(CwStatusRequest request, ServerCallContext context) =>
        ReadCwStatusAsync(null, context.CancellationToken);

    public override Task<CwRadioStatus> SetCwSpeed(CwSpeedRequest request, ServerCallContext context) =>
        ReadCwStatusAsync(request.Wpm, context.CancellationToken);

    private async Task<CwRadioStatus> ReadCwStatusAsync(double? wpm, CancellationToken ct)
    {
        await _commands.WaitAsync(ct);
        try
        {
            await RecoverOrphanedPttAsync(ct);
            if (_cwActive is not null && wpm.HasValue)
                throw new InvalidOperationException("CW message is active.");
            await using var keyer = await ConnectCwAsync(ct);
            if (wpm is double speed)
            {
                int expected = CwMessageTiming.WpmToRawKeySpeed(speed);
                var written = await keyer.SetWpmAsync(speed, ct);
                var read = await keyer.GetStatusAsync(ct);
                if (written.KeySpeedRaw != expected || read.KeySpeedRaw != expected)
                    throw new IOException("CW WPM write/readback mismatch.");
                return MapCw(read);
            }
            var current = await keyer.GetStatusAsync(ct);
            return MapCw(_cwActive is null ? current : current with { Lease = "BUSY" });
        }
        catch (ArgumentException e) { throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message)); }
        catch (InvalidOperationException e) { throw new RpcException(new Status(StatusCode.FailedPrecondition, e.Message)); }
        catch (Exception e) when (e is IOException or FormatException or TimeoutException or System.Net.Sockets.SocketException)
        { throw FailedCat(e); }
        finally { _commands.Release(); }
    }

    private static CwRadioStatus MapCw(CwKeyerStatus s) => new()
    {
        Lease = s.Lease, Mode = s.Mode, BreakIn = s.BreakIn,
        Transmitting = s.Transmitting, KeySpeedRaw = s.KeySpeedRaw,
        Wpm = s.Wpm, ActualTxFrequencyHz = s.ActualTxFrequencyHz ?? 0
    };
}
