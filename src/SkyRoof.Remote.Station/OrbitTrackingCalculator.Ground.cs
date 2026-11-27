using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class OrbitTrackingCalculator
{
    internal GroundTrackResult GroundTrack(GroundTrackRequest input, CancellationToken token)
    {
        var request = input.Tracking ?? throw new ArgumentException("Tracking request is required.");
        _ = PassSite(request.LatitudeDegrees, request.LongitudeDegrees, request.HeightMetres);
        var start = DateTimeOffset.FromUnixTimeMilliseconds(input.StartUtcMilliseconds);
        var end = DateTimeOffset.FromUnixTimeMilliseconds(input.EndUtcMilliseconds);
        if (end < start || input.StepSeconds <= 0 ||
            (end - start).TotalSeconds / input.StepSeconds > 2000)
            throw new ArgumentException("Ground track requires an ordered interval with at most 2001 samples.");
        IOrbitTrack track;
        string source;
        lock (_sync) (track, source) = ResolveTrack(request);
        var result = new GroundTrackResult { NoradNumber = track.NoradNumber,
            SiteLatitudeDegrees = request.LatitudeDegrees, SiteLongitudeDegrees = request.LongitudeDegrees,
            CelestialTarget = request.CelestialTarget };
        for (var utc = start; utc <= end; utc = utc.AddSeconds(input.StepSeconds))
        {
            token.ThrowIfCancellationRequested();
            result.Points.Add(GroundPoint(track, source, utc));
            if (utc == end) break;
            if ((end - utc).TotalSeconds < input.StepSeconds)
            {
                result.Points.Add(GroundPoint(track, source, end));
                break;
            }
        }
        return result;
    }

    private GroundTrackPoint GroundPoint(IOrbitTrack track, string source, DateTimeOffset utc)
    {
        lock (_sync)
        {
            var selected = _issOem.Select(backend, track, utc);
            var position = selected.Track.PredictGeodetic(utc);
            return new GroundTrackPoint
            {
                UtcMilliseconds = utc.ToUnixTimeMilliseconds(),
                LatitudeDegrees = position.LatitudeDegrees,
                LongitudeDegrees = position.LongitudeDegrees,
                HeightMetres = position.EllipsoidalHeightMetres,
                OrbitBackend = selected.Backend, DataQuality = source + selected.QualityPrefix
            };
        }
    }
}
