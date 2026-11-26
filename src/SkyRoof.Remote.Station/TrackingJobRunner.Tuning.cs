using Grpc.Core;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class TrackingJobRunner
{
    internal async Task<TrackingJobStatus> TuneAsync(TrackingJobTune input, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_request is null || !_status.Running || input.JobId != _status.JobId)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Tracking job stopped or replaced."));
            var updated = _request.Clone();
            var config = updated.Tracking;
            if (config.CelestialTarget.Length != 0)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Satellite RadioLink offsets require a satellite job."));
            var link = config.RadioLink ?? new RadioLinkProfile
            {
                DownlinkLowHz = config.DownlinkHz, UplinkLowHz = config.UplinkHz
            };
            ApplyOffsets(link, input);
            config.RadioLink = link;
            Validate(updated);
            if (updated.ResumeAfterRestart) store.Save(_status.JobId, updated);
            _request = updated;
            _status.Phase = "retuning";
            _status.Latest = null;
            _nextTick = 0;
            return _status.Clone();
        }
        finally { _gate.Release(); }
    }

    private static void ApplyOffsets(RadioLinkProfile link, TrackingJobTune input)
    {
        if (input.HasTransponderOffsetHz) link.TransponderOffsetHz = input.TransponderOffsetHz;
        if (input.HasRitHz) link.RitHz = input.RitHz;
        if (input.HasXitHz) link.XitHz = input.XitHz;
        if (input.HasDownlinkManualHz) link.DownlinkManualHz = input.DownlinkManualHz;
        if (input.HasUplinkManualHz) link.UplinkManualHz = input.UplinkManualHz;
        if (input.HasDownlinkBaseOffsetHz) link.DownlinkBaseOffsetHz = input.DownlinkBaseOffsetHz;
        if (input.HasUplinkBaseOffsetHz) link.UplinkBaseOffsetHz = input.UplinkBaseOffsetHz;
    }
}
