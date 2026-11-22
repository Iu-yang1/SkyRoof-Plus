using System.Globalization;
using System.Net.Sockets;
using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Device;

public sealed class RotatorService : DeviceRotator.DeviceRotatorBase, IAsyncDisposable
{
    private readonly RigctldLink? _link;
    private readonly RigctldLink? _stopLink;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _feedbackSync = new();
    private readonly double _minAz, _maxAz, _minEl, _maxEl;
    private RotatorPosition? _lastReadback;
    private long _stopRevision;

    public RotatorService()
    {
        static double Limit(string name, double fallback) =>
            double.TryParse(Environment.GetEnvironmentVariable(name),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value : fallback;
        _minAz = Limit("SKYROOF_ROTATOR_AZ_MIN", 0);
        _maxAz = Limit("SKYROOF_ROTATOR_AZ_MAX", 450);
        _minEl = Limit("SKYROOF_ROTATOR_EL_MIN", 0);
        _maxEl = Limit("SKYROOF_ROTATOR_EL_MAX", 180);
        if (_minAz >= _maxAz || _minEl >= _maxEl ||
            !double.IsFinite(_minAz + _maxAz + _minEl + _maxEl))
            throw new ArgumentException("Invalid rotator mechanical limits.");

        var host = Environment.GetEnvironmentVariable("SKYROOF_ROTATOR_HOST");
        if (!string.IsNullOrWhiteSpace(host))
        {
            var port = int.TryParse(Environment.GetEnvironmentVariable("SKYROOF_ROTATOR_PORT"),
                out var configured) ? configured : 4533;
            _link = new RigctldLink(host, port);
            // Hamlib rotctld supports multiple clients. STOP must not sit behind
            // an ordinary position read timeout on the station's primary link.
            _stopLink = new RigctldLink(host, port);
        }
    }

    public override Task<RotatorPosition> Read(RotatorRead request, ServerCallContext context) =>
        ExecuteAsync(null, context.CancellationToken);

    public override async Task<RotatorPosition> Stop(RotatorRead request, ServerCallContext context)
    {
        Interlocked.Increment(ref _stopRevision);
        var primary = _link ?? throw new RpcException(
            new Status(StatusCode.FailedPrecondition, "Rotator endpoint not configured."));
        try
        {
            // Independent TCP session preempts blocked primary readback.
            await (_stopLink ?? primary).WriteAsync("S", context.CancellationToken);
        }
        catch (Exception first) when (first is IOException or SocketException or OperationCanceledException
            && !context.CancellationToken.IsCancellationRequested)
        {
            // Some rotctld-compatible implementations admit only one client.
            // Retain the serial primary STOP fallback rather than locking the user out.
            try { await primary.WriteAsync("S", context.CancellationToken); }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                throw new RpcException(new Status(StatusCode.Unavailable,
                    "Rotator STOP command unconfirmed: " + ex.Message));
            }
        }
        lock (_feedbackSync)
        {
            return new RotatorPosition
            {
                AzimuthDegrees = _lastReadback?.AzimuthDegrees ?? 0,
                ElevationDegrees = _lastReadback?.ElevationDegrees ?? 0,
                HasPosition = _lastReadback is not null,
                ReadbackFresh = false,
                StopTransmitted = true
            };
        }
    }

    public override Task<RotatorPosition> Point(RotatorPoint request, ServerCallContext context)
    {
        if (!double.IsFinite(request.AzimuthDegrees) ||
            !double.IsFinite(request.ElevationDegrees) ||
            request.AzimuthDegrees < _minAz || request.AzimuthDegrees > _maxAz ||
            request.ElevationDegrees < _minEl || request.ElevationDegrees > _maxEl)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Bearing outside configured mechanical travel"));
        var revision = Volatile.Read(ref _stopRevision);
        return ExecuteAsync(FormattableString.Invariant(
            $"P {request.AzimuthDegrees:F3} {request.ElevationDegrees:F3}"),
            context.CancellationToken, revision);
    }

    private async Task<RotatorPosition> ExecuteAsync(string? command, CancellationToken token,
        long? revision = null)
    {
        var link = _link ?? throw new RpcException(
            new Status(StatusCode.FailedPrecondition, "Rotator endpoint not configured"));
        await _gate.WaitAsync(token);
        try
        {
            if (revision is not null && revision.Value != Volatile.Read(ref _stopRevision))
                throw new RpcException(new Status(StatusCode.Aborted, "Point superseded by STOP."));
            if (command is not null) await link.WriteAsync(command, token);
            var lines = (await link.ReadPositionAsync(token)).Split('\n');
            if (lines.Length != 2 ||
                !double.TryParse(lines[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var az) ||
                !double.TryParse(lines[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var el) ||
                !double.IsFinite(az) || !double.IsFinite(el))
                throw new IOException("Invalid rotctld position reply.");
            if (revision is not null && revision.Value != Volatile.Read(ref _stopRevision))
                throw new RpcException(new Status(StatusCode.Aborted, "Point superseded by STOP."));
            var position = new RotatorPosition
            {
                AzimuthDegrees = az, ElevationDegrees = el,
                HasPosition = true, ReadbackFresh = true
            };
            lock (_feedbackSync) _lastReadback = position;
            return position;
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, ex.Message));
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_link is not null) await _link.DisposeAsync();
        if (_stopLink is not null) await _stopLink.DisposeAsync();
        _gate.Dispose();
    }
}
