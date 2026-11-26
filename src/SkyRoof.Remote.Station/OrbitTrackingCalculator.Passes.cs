using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class OrbitTrackingCalculator
{
    internal PassSearchResult PlanPasses(PassSearchRequest input, CancellationToken token)
    {
        var request = input.Tracking ?? throw new ArgumentException("Tracking site is required.");
        var start = DateTimeOffset.FromUnixTimeMilliseconds(input.StartUtcMilliseconds);
        var end = DateTimeOffset.FromUnixTimeMilliseconds(input.EndUtcMilliseconds);
        if (end <= start || end - start > TimeSpan.FromHours(72))
            throw new ArgumentException("Select a search interval up to 72 hours.");
        if (!double.IsFinite(request.ElevationMaskDegrees) || request.ElevationMaskDegrees is < 0 or > 90)
            throw new ArgumentException("Pass mask must be 0–90 degrees.");
        var local = PassSite(request.LatitudeDegrees, request.LongitudeDegrees, request.HeightMetres);
        GroundStationSpec? peer = null;
        if (input.CommonMoonVisibility)
        {
            if (!request.CelestialTarget.Equals("MOON", StringComparison.OrdinalIgnoreCase) || request.EmePeer is not { } dx)
                throw new ArgumentException("Common Moon visibility requires a DX ground site.");
            peer = PassSite(dx.LatitudeDegrees, dx.LongitudeDegrees, dx.HeightMetres);
        }
        IOrbitTrack track;
        string source;
        lock (_sync) (track, source) = ResolveTrack(request);
        PassSkyPoint Observe(DateTimeOffset utc)
        {
            token.ThrowIfCancellationRequested();
            return ObservePassPoint(track, source, local, peer, utc);
        }
        double Elevation(DateTimeOffset utc)
        {
            var point = Observe(utc);
            return point.HasPeerElevationDegrees ? Math.Min(point.ElevationDegrees, point.PeerElevationDegrees) : point.ElevationDegrees;
        }
        var passes = OrbitPassPredictor.FindPasses(Elevation, start, end, request.ElevationMaskDegrees);
        var result = new PassSearchResult { CommonMoonVisibility = input.CommonMoonVisibility };
        foreach (var pass in passes) result.Passes.Add(BuildWindow(pass, Observe));
        return result;
    }

    private static GroundStationSpec PassSite(double lat, double lon, double height)
    {
        if (!double.IsFinite(lat) || lat is < -90 or > 90 ||
            !double.IsFinite(lon) || lon is < -180 or > 180 || !double.IsFinite(height))
            throw new ArgumentException("Invalid ground site.");
        return new GroundStationSpec(lat, lon, height);
    }

    private PassSkyPoint ObservePassPoint(IOrbitTrack track, string source, GroundStationSpec local,
        GroundStationSpec? peer, DateTimeOffset utc)
    {
        lock (_sync)
        {
            var selected = _issOem.Select(backend, track, utc);
            var observed = selected.Track.Observe(local, utc);
            var point = new PassSkyPoint
            {
                UtcMilliseconds = utc.ToUnixTimeMilliseconds(),
                AzimuthDegrees = observed.AzimuthDegrees, ElevationDegrees = observed.ElevationDegrees,
                OrbitBackend = selected.Backend, DataQuality = source + selected.QualityPrefix + observed.DataQuality
            };
            if (peer is { } dx) point.PeerElevationDegrees = selected.Track.Observe(dx, utc).ElevationDegrees;
            return point;
        }
    }

    private static PassWindow BuildWindow(OrbitPass pass, Func<DateTimeOffset, PassSkyPoint> observe)
    {
        var result = new PassWindow
        {
            AosUtcMilliseconds = pass.StartUtc.ToUnixTimeMilliseconds(),
            LosUtcMilliseconds = pass.EndUtc.ToUnixTimeMilliseconds(),
            CulminationUtcMilliseconds = pass.CulminationUtc.ToUnixTimeMilliseconds(),
            MaximumElevationDegrees = pass.MaximumElevationDegrees,
            ActualAos = pass.ActualAos, ActualLos = pass.ActualLos
        };
        // Include culmination explicitly. Ordered and unique sample instants;
        // the path is for visualization, not motor control.
        var times = new SortedSet<DateTimeOffset> { pass.CulminationUtc };
        for (int i = 0; i <= 30; i++)
            times.Add(pass.StartUtc.AddTicks((pass.EndUtc - pass.StartUtc).Ticks * i / 30));
        foreach (var utc in times) result.SkyPath.Add(observe(utc));
        return result;
    }
}
