using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

internal static class TrackingRadioLink
{
    internal static (long Rx, long Tx) Calculate(TrackingRequest request, double factor)
    {
        double rxFactor = request.CorrectDownlink ? factor : 0;
        double txFactor = request.CorrectUplink ? factor : 0;
        if (request.RadioLink is not { } link)
            return (Rounded(RadioLinkMath.CorrectDownlink(request.DownlinkHz, rxFactor, 0, 0)),
                Rounded(RadioLinkMath.CorrectUplink(request.UplinkHz, txFactor, 0, 0)));
        var band = new SatellitePassband(Edge(link.DownlinkLowHz), Edge(link.DownlinkHighHz),
            Edge(link.UplinkLowHz), Edge(link.UplinkHighHz), link.Inverting);
        double rx = band.DownlinkNominal(link.DownlinkBaseOffsetHz, link.TransponderOffsetHz);
        double tx = band.UplinkNominal(link.UplinkBaseOffsetHz, link.TransponderOffsetHz);
        return (band.DownlinkLowHz.HasValue
                ? Rounded(RadioLinkMath.CorrectDownlink(rx, rxFactor, link.RitHz, link.DownlinkManualHz)) : 0,
            band.UplinkLowHz.HasValue
                ? Rounded(RadioLinkMath.CorrectUplink(tx, txFactor, link.XitHz, link.UplinkManualHz)) : 0);
    }

    private static long? Edge(long hz) => hz switch
    {
        0 => null,
        > 0 => hz,
        _ => throw new ArgumentException("RadioLink passband edges cannot be negative.")
    };

    private static long Rounded(double hz)
    {
        if (!double.IsFinite(hz) || hz < 0)
            throw new ArgumentException("RadioLink produced an invalid RF frequency.");
        return checked((long)Math.Round(hz));
    }
}
