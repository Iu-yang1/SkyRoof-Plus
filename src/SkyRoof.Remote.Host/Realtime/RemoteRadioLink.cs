using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteRadioLink
{
    public long DownlinkLowHz { get; init; }
    public long DownlinkHighHz { get; init; }
    public long UplinkLowHz { get; init; }
    public long UplinkHighHz { get; init; }
    public bool Inverting { get; init; }
    public long DownlinkBaseOffsetHz { get; init; }
    public long UplinkBaseOffsetHz { get; init; }
    public double TransponderOffsetHz { get; init; }
    public double RitHz { get; init; }
    public double XitHz { get; init; }
    public double DownlinkManualHz { get; init; }
    public double UplinkManualHz { get; init; }

    internal static RemoteRadioLink FromIpc(RadioLinkProfile link) => new()
    {
        DownlinkLowHz = link.DownlinkLowHz, DownlinkHighHz = link.DownlinkHighHz,
        UplinkLowHz = link.UplinkLowHz, UplinkHighHz = link.UplinkHighHz, Inverting = link.Inverting,
        DownlinkBaseOffsetHz = link.DownlinkBaseOffsetHz, UplinkBaseOffsetHz = link.UplinkBaseOffsetHz,
        TransponderOffsetHz = link.TransponderOffsetHz, RitHz = link.RitHz, XitHz = link.XitHz,
        DownlinkManualHz = link.DownlinkManualHz, UplinkManualHz = link.UplinkManualHz
    };

    internal RadioLinkProfile ToIpc() => new()
    {
        DownlinkLowHz = DownlinkLowHz, DownlinkHighHz = DownlinkHighHz,
        UplinkLowHz = UplinkLowHz, UplinkHighHz = UplinkHighHz, Inverting = Inverting,
        DownlinkBaseOffsetHz = DownlinkBaseOffsetHz, UplinkBaseOffsetHz = UplinkBaseOffsetHz,
        TransponderOffsetHz = TransponderOffsetHz, RitHz = RitHz, XitHz = XitHz,
        DownlinkManualHz = DownlinkManualHz, UplinkManualHz = UplinkManualHz
    };
}
