using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class OrbitTrackingCalculator
{
    private readonly IOrbitBackend backend;
    private readonly StationCatalogCache catalog;

    public OrbitTrackingCalculator(IOrbitBackend backend, StationCatalogCache catalog)
    {
        this.backend = backend;
        this.catalog = catalog;
    }

    private readonly object _sync = new();
    private string? _line1, _line2;
    private OrbitMeanElements? _meanElements;
    private IOrbitTrack? _track;
    private readonly IssOemSource _issOem = new();

    internal TrackingSnapshot Predict(TrackingRequest request)
    {
        if (!double.IsFinite(request.LatitudeDegrees) ||
            request.LatitudeDegrees is < -90 or > 90 ||
            !double.IsFinite(request.LongitudeDegrees) ||
            request.LongitudeDegrees is < -180 or > 180 ||
            !double.IsFinite(request.HeightMetres) ||
            !double.IsFinite(request.ElevationMaskDegrees) ||
            (request.RadioLink is null && (request.DownlinkHz <= 0 || request.UplinkHz < 0)))
            throw new ArgumentException("Invalid ground site or tracking frequencies.");

        var utc = DateTimeOffset.FromUnixTimeMilliseconds(request.UtcUnixMilliseconds);
        OrbitObservation observed;
        int noradNumber;
        string orbitBackend, qualityPrefix;
        lock (_sync)
        {
            var (track, source) = ResolveTrack(request, utc);
            noradNumber = track.NoradNumber;
            var selected = _issOem.Select(backend, track, utc);
            orbitBackend = selected.Backend;
            qualityPrefix = source + selected.QualityPrefix;
            observed = selected.Track.Observe(new GroundStationSpec(request.LatitudeDegrees,
                request.LongitudeDegrees, request.HeightMetres), utc);
        }
        var factor = observed.RangeRateMetresPerSecond / RadioLinkMath.SpeedOfLightMetresPerSecond;
        var frequencies = TrackingRadioLink.Calculate(request, factor);
        if (frequencies.Rx == 0 && frequencies.Tx == 0)
            throw new ArgumentException("RadioLink requires at least one frequency.");
        return new TrackingSnapshot
        {
            NoradNumber = noradNumber,
            UtcUnixMilliseconds = utc.ToUnixTimeMilliseconds(),
            AzimuthDegrees = observed.AzimuthDegrees,
            ElevationDegrees = observed.ElevationDegrees,
            RangeMetres = observed.RangeMetres,
            RangeRateMetresPerSecond = observed.RangeRateMetresPerSecond,
            RxHz = frequencies.Rx,
            TxHz = frequencies.Tx,
            OrbitBackend = orbitBackend,
            DataQuality = qualityPrefix + observed.DataQuality
        };
    }

}
