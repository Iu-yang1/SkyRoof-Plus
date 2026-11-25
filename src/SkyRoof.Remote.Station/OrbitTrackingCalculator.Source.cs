using SkyRoof.Orbit.Contracts;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.Station;

public sealed partial class OrbitTrackingCalculator
{
    private IOrbitTrack? _moon;

    private (IOrbitTrack Track, string Source) ResolveTrack(TrackingRequest request)
    {
        if (request.CelestialTarget.Length > 0)
        {
            if (!request.CelestialTarget.Equals("MOON", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unsupported celestial tracking target.");
            if (backend is not ICelestialOrbitBackend celestial)
                throw new ArgumentException("Moon tracking requires Orekit/JPL ephemeris.");
            _moon ??= celestial.CreateCelestialTrack("MOON");
            return (_moon, "MOON_DE430;");
        }
        CatalogCacheTle? orbit = request.CatalogSatelliteId.Length == 0 ? null :
            catalog.ResolveOrbit(request.CatalogSatelliteId, DateTimeOffset.UtcNow);
        var omm = orbit?.ReadOmm();
        if (omm is not null)
        {
            if (_track is null || _meanElements != omm)
            {
                _track = backend.CreateOmmTrack(omm);
                _meanElements = omm;
                _line1 = _line2 = null;
            }
            return (_track, "CATALOG_OMM;");
        }

        string line1 = orbit?.Tle1 ?? request.TleLine1;
        string line2 = orbit?.Tle2 ?? request.TleLine2;
        if (_track is null || _meanElements is not null || _line1 != line1 || _line2 != line2)
        {
            _track = backend.CreateTleTrack(line1, line2);
            _line1 = line1;
            _line2 = line2;
            _meanElements = null;
        }
        return (_track, orbit is null ? "" : "CATALOG_TLE;");
    }
}
