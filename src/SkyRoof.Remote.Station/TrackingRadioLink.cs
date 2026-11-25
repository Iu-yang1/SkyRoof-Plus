using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

internal static class TrackingRadioLink
{
    internal static (long Rx, long Tx) Calculate(TrackingRequest request, double factor, EmePathStatus? path = null)
    {
        double rxFactor = request.CorrectDownlink ? factor : 0;
        double txFactor = request.CorrectUplink ? factor : 0;
        if (request.CelestialTarget.Equals("MOON", StringComparison.OrdinalIgnoreCase))
            return path is null ? CalculateMoon(request, factor) : CalculateDxFixed(request, path);
        if (request.EmeDopplerMode.Length > 0)
            throw new ArgumentException("EME Doppler mode requires Moon target.");
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

    private static (long Rx, long Tx) CalculateMoon(TrackingRequest request, double factor)
    {
        double rxFactor = request.CorrectDownlink ? factor : 0;
        double txFactor = request.CorrectUplink ? factor : 0;
        (double rx, double tx) = request.EmeDopplerMode switch
        {
            "cfom" => (rxFactor, txFactor),
            "own-echo-rx" => (2 * rxFactor, 0),
            "own-echo-tx" => (0, 2 * txFactor),
            "none" => (0, 0),
            _ => throw new ArgumentException("Select Moon Doppler mode explicitly.")
        };
        return (Rounded(RadioLinkMath.CorrectDownlink(request.DownlinkHz, rx, 0, 0)),
            Rounded(RadioLinkMath.CorrectUplink(request.UplinkHz, tx, 0, 0)));
    }

    private static (long Rx, long Tx) CalculateDxFixed(TrackingRequest request, EmePathStatus path)
    {
        double rx = request.CorrectDownlink ? request.DownlinkHz * path.ReceiveRatio : request.DownlinkHz;
        double tx = request.CorrectUplink ? request.UplinkHz / path.TransmitRatio : request.UplinkHz;
        return (Rounded(rx), Rounded(tx));
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
