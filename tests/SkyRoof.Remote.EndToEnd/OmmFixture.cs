using Microsoft.AspNetCore.SignalR.Client;
using SGPdotNET.Parsers;
using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.SgpNet;
using SkyRoof.Remote.Host.Realtime;

internal static class OmmFixture
{
    internal static OmmData Data() => new()
    {
        NoradCatID = 100001, ObjectID = "2026-001A", ClassificationType = "U",
        Epoch = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
        MeanMotion = 15.5, MeanMotionDot = 0.00001, MeanMotionDDot = 0,
        Eccentricity = 0.001, Inclination = 51.6, RAOfAscNode = 120,
        ArgOfPericenter = 80, MeanAnomaly = 25, BStar = 0.0001,
        EphemerisType = 0, ElementSetNo = 999, RevAtEpoch = 12345
    };

    internal static async Task RunAsync(HubConnection hub)
    {
        var epoch = new DateTimeOffset(Data().Epoch);
        var elements = new OrbitMeanElements(100001, "2026-001A", 'U', epoch,
            15.5, 0.00001, 0, 0.001, 51.6, 120, 80, 25, 0.0001, 0, 999, 12345);
        var baseline = new SgpNetOrbitBackend().CreateOmmTrack(elements);
        var utc = epoch.AddMinutes(10);
        var ground = new GroundStationSpec(28.5, 115.9, 40);
        var reference = baseline.Observe(ground, utc);
        var request = new RemoteTrackingRequest
        {
            CatalogSatelliteId = "fixture-100001", LatitudeDegrees = ground.LatitudeDegrees,
            LongitudeDegrees = ground.LongitudeDegrees, HeightMetres = ground.EllipsoidalHeightMetres,
            UtcUnixMilliseconds = utc.ToUnixTimeMilliseconds(), DownlinkHz = 145900000,
            CorrectDownlink = true
        };
        var actual = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking", request);
        if (actual.NoradNumber != 100001 || !actual.DataQuality.Contains("CATALOG_OMM;", StringComparison.Ordinal) ||
            Math.Abs(actual.RangeMetres - reference.RangeMetres) > 3000 ||
            Math.Abs(actual.RangeRateMetresPerSecond - reference.RangeRateMetresPerSecond) > 5 ||
            actual.CatApplied || actual.RotatorApplied)
            throw new InvalidDataException("Six-digit OMM catalog prediction differs from independent SGP.NET.");
        var tle = await hub.InvokeAsync<RemoteTrackingStatus>("PredictTracking",
            TrackingChecks.Request() with { CatalogSatelliteId = "fixture-00005", TleLine1 = "", TleLine2 = "" });
        if (tle.NoradNumber != 5 || !tle.DataQuality.Contains("CATALOG_TLE;", StringComparison.Ordinal))
            throw new InvalidDataException("Station did not restore automatic TLE after manual expiry.");
        Console.WriteLine("PASS: authenticated catalog OMM NORAD 100001, direct SGP4, independent range/range-rate, automatic TLE fallback.");
    }
}
