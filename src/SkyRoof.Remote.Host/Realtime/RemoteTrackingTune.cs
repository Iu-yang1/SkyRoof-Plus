using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteTrackingTune
{
    public string JobId { get; init; } = "";
    public double? TransponderOffsetHz { get; init; }
    public double? RitHz { get; init; }
    public double? XitHz { get; init; }
    public double? DownlinkManualHz { get; init; }
    public double? UplinkManualHz { get; init; }
    public long? DownlinkBaseOffsetHz { get; init; }
    public long? UplinkBaseOffsetHz { get; init; }

    internal TrackingJobTune ToIpc()
    {
        var input = new TrackingJobTune { JobId = JobId };
        if (TransponderOffsetHz is { } position) input.TransponderOffsetHz = position;
        if (RitHz is { } rit) input.RitHz = rit;
        if (XitHz is { } xit) input.XitHz = xit;
        if (DownlinkManualHz is { } rx) input.DownlinkManualHz = rx;
        if (UplinkManualHz is { } tx) input.UplinkManualHz = tx;
        if (DownlinkBaseOffsetHz is { } rxBase) input.DownlinkBaseOffsetHz = rxBase;
        if (UplinkBaseOffsetHz is { } txBase) input.UplinkBaseOffsetHz = txBase;
        return input;
    }
}
