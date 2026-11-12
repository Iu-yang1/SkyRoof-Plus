using Serilog;
using System.Collections.Concurrent;
using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.TLE;
using SGPdotNET.Util;
using SkyRoof.Orbit.Contracts;

namespace SkyRoof
{
  public class SatelliteTracker
  {
    private IOrbitTrack? orbitTrack;
    private readonly CelestialBodyId? EphemerisBody;

    public Tle? Tle;
    // The same expired or malformed orbit may be rehydrated into a new
    // tracker after every refresh. Quarantine by element identity, never by
    // NORAD alone, so fresh ephemerides get another chance immediately.
    private static readonly ConcurrentDictionary<string,byte> InvalidElements = new();
    private const int MaxInvalidElements = 4096;
    private readonly string? orbitElementKey;
    public bool Enabled { get; private set; }
    internal bool IsCelestialEphemeris => EphemerisBody.HasValue;

    public SatelliteTracker(SatnogsDbTle? tle)
    {
      if (tle == null) return;
      orbitElementKey = BuildOrbitElementKey(tle);
      if (InvalidElements.ContainsKey(orbitElementKey)) return;
      // This Tle object supplies legacy orbit-number metadata only, not propagation.
      Tle = CreateTle(tle);
      if (Tle == null) return;
      try
      {
        orbitTrack = OrbitBackendRegistry.CreateTrack(tle)
          ?? throw new InvalidOperationException("Orekit engine not initialized.");
        Enabled = true;
      }
      catch (Exception ex)
      {
        LogOrbitFailure(ex, $"Orekit track creation failed for NORAD {tle.norad_cat_id}.");
      }
    }

    internal SatelliteTracker(IOrbitTrack celestialTrack, CelestialBodyId body)
    {
      orbitTrack = celestialTrack;
      EphemerisBody = body;
      Enabled = true;
    }

    public Tle? CreateTle(SatnogsDbTle tle)
    {
      try
      {
        if (tle.omm != null)
          return new Tle(tle.omm);

        return new Tle(tle.tle0, tle.tle1, tle.tle2);
      }
      catch (Exception ex)
      {
        string payload =
          tle.omm != null
            ? $"OMM NORAD {tle.omm.NoradCatID} epoch {tle.omm.Epoch:O}"
            : $"|{tle.tle0}|{tle.tle1}|{tle.tle2}|";
        LogOrbitFailure(ex, $"Error creating orbit object: {payload}.");
        return null;
      }
    }

    public GeodeticCoordinate? Predict(DateTime? utc = null)
    {
      if (!Enabled || IsCelestialEphemeris) return null;
      try
      {
        var geo = (orbitTrack ?? throw new InvalidOperationException("Orekit track missing"))
          .PredictGeodetic(new DateTimeOffset((utc ?? DateTime.UtcNow).ToUniversalTime()));
        return new GeodeticCoordinate(
          Angle.FromDegrees(geo.LatitudeDegrees),
          Angle.FromDegrees(geo.LongitudeDegrees),
          geo.EllipsoidalHeightMetres / 1000.0);
      }
      catch (Exception ex)
      {
        Enabled = false;
        LogOrbitFailure(ex, $"Orekit Predict failed for NORAD {Tle?.NoradNumber}.");
        return null;
      }
    }

    internal TopocentricObservation? Observe(GroundStation groundStation, DateTime utc)
    {
      if (!Enabled) return null;
      try
      {
        var geo = groundStation.Location.ToGeodetic();
        var site = new GroundStationSpec(
          geo.Latitude.Degrees, geo.Longitude.Degrees, geo.Altitude * 1000.0);
        var obs = (orbitTrack ?? throw new InvalidOperationException("Orekit track missing"))
          .Observe(site, new DateTimeOffset(utc.ToUniversalTime()));
        return new TopocentricObservation(
          Angle.FromDegrees(obs.AzimuthDegrees),
          Angle.FromDegrees(obs.ElevationDegrees),
          obs.RangeMetres / 1000.0,
          obs.RangeRateMetresPerSecond / 1000.0,
          groundStation.Location);
      }
      catch (Exception ex)
      {
        LogOrbitFailure(ex, $"Orekit Observe failed for {EphemerisBody?.ToString() ?? Tle?.NoradNumber.ToString()}.");
        return null;
      }
    }

    internal List<SatelliteVisibilityPeriod> ComputePasses(
      GroundStation groundStation, DateTime startTime, DateTime endTime)
    {
      if (!Enabled) return new();
      if (IsCelestialEphemeris)
        return ComputeEphemerisPasses(groundStation, startTime, endTime);

      try
      {
        var geo = groundStation.Location.ToGeodetic();
        var site = new GroundStationSpec(
          geo.Latitude.Degrees, geo.Longitude.Degrees, geo.Altitude * 1000.0);
        return (orbitTrack ?? throw new InvalidOperationException("Orekit track missing"))
          .FindPasses(site,
            new DateTimeOffset(startTime.ToUniversalTime()),
            new DateTimeOffset(endTime.ToUniversalTime()))
          .Select(p => new SatelliteVisibilityPeriod(
            null!, p.StartUtc.UtcDateTime, p.EndUtc.UtcDateTime,
            Angle.FromDegrees(p.MaximumElevationDegrees),
            p.CulminationUtc.UtcDateTime, groundStation.Location))
          .ToList();
      }
      catch (Exception ex)
      {
        LogOrbitFailure(ex, $"Orekit ComputePasses failed for NORAD {Tle?.NoradNumber}.");
        return new();
      }
    }

    internal static string BuildOrbitElementKey(SatnogsDbTle element) =>
      element.omm is { } omm
        ? $"OMM:{element.norad_cat_id}:{omm.Epoch:O}:{omm.MeanMotion}:{omm.Eccentricity}"
        : $"TLE:{element.norad_cat_id}:{element.tle1}:{element.tle2}";

    internal static bool QuarantineOrbitKey(string key)
    {
      if (InvalidElements.Count >= MaxInvalidElements)
        InvalidElements.Clear(); // bounds the lifetime cache; never block new elements.
      return InvalidElements.TryAdd(key, 1);
    }

    private void LogOrbitFailure(Exception exception, string context)
    {
      if (orbitElementKey == null)
      {
        Log.Error(exception, "{OrbitIssue}", context);
        return;
      }
      if (QuarantineOrbitKey(orbitElementKey))
        Log.Warning(
          "Orbit elements quarantined for this process (source failure logged once). {Context} {ExceptionType}: {Message}",
          context, exception.GetType().Name, exception.Message);
      else
        Log.Debug("Repeated invalid orbit skipped: {Context}", context);
    }

    private List<SatelliteVisibilityPeriod> ComputeEphemerisPasses(
      GroundStation groundStation,
      DateTime startTime,
      DateTime endTime)
    {
      var result = new List<SatelliteVisibilityPeriod>();
      if (startTime >= endTime) return result;

      TimeSpan step = TimeSpan.FromMinutes(2);
      DateTime previousTime = startTime;
      TopocentricObservation? previous =
        Observe(groundStation, previousTime);
      bool previousAbove =
        previous != null &&
        previous.Elevation.Radians >= 0;

      DateTime? passStart = null;

      if (previousAbove)
      {
        // Match SGP.NET's clipToStartTime:false semantics. When the search
        // begins while Moon/Sun/Venus is already above the horizon, recover
        // the real AOS instead of calling startTime the pass start. A stable
        // AOS is important for pass identity across periodic rebuilds.
        DateTime upper = startTime;
        DateTime lower = upper - step;

        for (int i = 0; i < 8 * 60; i++)
        {
          TopocentricObservation? obs =
            Observe(groundStation, lower);

          if (obs == null ||
              obs.Elevation.Radians < 0)
          {
            passStart =
              RefineHorizonCrossing(
                groundStation,
                lower,
                upper,
                rising: true);
            break;
          }

          upper = lower;
          lower -= step;
        }

        // Circumpolar/polar-day fallback: the body may remain above the
        // horizon throughout our bounded search. Keep the requested start in
        // that exceptional case rather than dropping the target entirely.
        passStart ??= startTime;
      }

      for (DateTime t = startTime + step;
           t <= endTime;
           t += step)
      {
        TopocentricObservation? current =
          Observe(groundStation, t);
        bool currentAbove =
          current != null &&
          current.Elevation.Radians >= 0;

        if (!previousAbove && currentAbove)
        {
          passStart = RefineHorizonCrossing(
            groundStation,
            previousTime,
            t,
            rising: true);
        }
        else if (previousAbove &&
                 !currentAbove &&
                 passStart != null)
        {
          DateTime passEnd =
            RefineHorizonCrossing(
              groundStation,
              previousTime,
              t,
              rising: false);

          result.Add(
            BuildVisibilityPeriod(
              groundStation,
              passStart.Value,
              passEnd));

          passStart = null;
        }

        previousTime = t;
        previousAbove = currentAbove;
      }

      if (passStart != null && previousAbove)
      {
        DateTime probe = endTime;
        DateTime passEnd = endTime;

        // Continue at most 16 hours so a pass that crosses the caller's end
        // remains a complete rise/set interval like SGP.NET visibility periods.
        for (int i = 0; i < 8 * 60; i++)
        {
          DateTime next =
            probe + step;
          TopocentricObservation? obs =
            Observe(groundStation, next);
          bool above =
            obs != null &&
            obs.Elevation.Radians >= 0;

          if (!above)
          {
            passEnd = RefineHorizonCrossing(
              groundStation,
              probe,
              next,
              rising: false);
            break;
          }

          probe = next;
          passEnd = probe;
        }

        result.Add(
          BuildVisibilityPeriod(
            groundStation,
            passStart.Value,
            passEnd));
      }

      return result;
    }

    private SatelliteVisibilityPeriod BuildVisibilityPeriod(
      GroundStation groundStation,
      DateTime start,
      DateTime end)
    {
      if (end <= start)
        end =
          start + TimeSpan.FromSeconds(1);

      TimeSpan duration = end - start;
      TimeSpan coarseStep =
        duration > TimeSpan.FromHours(2)
          ? TimeSpan.FromMinutes(5)
          : TimeSpan.FromMinutes(1);

      DateTime maxTime = start;
      double maxElevation =
        double.NegativeInfinity;

      for (DateTime t = start;
           t <= end;
           t += coarseStep)
      {
        double elevation =
          Observe(groundStation, t)?
            .Elevation.Radians
          ?? double.NegativeInfinity;

        if (elevation > maxElevation)
        {
          maxElevation = elevation;
          maxTime = t;
        }
      }

      DateTime before =
        maxTime - coarseStep < start
          ? start
          : maxTime - coarseStep;
      DateTime after =
        maxTime + coarseStep > end
          ? end
          : maxTime + coarseStep;

      for (int i = 0; i < 20; i++)
      {
        TimeSpan span = after - before;
        DateTime t1 =
          before +
          TimeSpan.FromTicks(
            span.Ticks / 3);
        DateTime t2 =
          after -
          TimeSpan.FromTicks(
            span.Ticks / 3);

        double e1 =
          Observe(groundStation, t1)?
            .Elevation.Radians
          ?? double.NegativeInfinity;
        double e2 =
          Observe(groundStation, t2)?
            .Elevation.Radians
          ?? double.NegativeInfinity;

        if (e1 < e2)
          before = t1;
        else
          after = t2;
      }

      maxTime =
        before +
        TimeSpan.FromTicks(
          (after - before).Ticks / 2);
      maxElevation =
        Observe(groundStation, maxTime)?
          .Elevation.Radians
        ?? 0;

      return new SatelliteVisibilityPeriod(
        null!,
        start,
        end,
        Angle.FromRadians(maxElevation),
        maxTime,
        groundStation.Location);
    }

    private DateTime RefineHorizonCrossing(
      GroundStation groundStation,
      DateTime a,
      DateTime b,
      bool rising)
    {
      for (int i = 0; i < 28; i++)
      {
        DateTime midpoint =
          a +
          TimeSpan.FromTicks(
            (b - a).Ticks / 2);

        double elevation =
          Observe(groundStation, midpoint)?
            .Elevation.Radians
          ?? -Math.PI / 2;

        if (rising)
        {
          if (elevation >= 0)
            b = midpoint;
          else
            a = midpoint;
        }
        else
        {
          if (elevation >= 0)
            a = midpoint;
          else
            b = midpoint;
        }
      }

      return rising ? b : a;
    }

    internal bool IsGeoStationary()
    {
      if (!Enabled || IsCelestialEphemeris)
        return false;

      return
        Math.Abs(
          Tle!.MeanMotionRevPerDay -
          1) <
        0.1f;
    }

    internal List<SatelliteVisibilityPeriod> ComputeGeostationaryPasses(
      GroundStation groundStation)
    {
      var observation =
        Observe(
          groundStation,
          DateTime.UtcNow);

      if (observation == null ||
          observation.Elevation.Radians < 0)
        return new();

      return new()
      {
        new SatelliteVisibilityPeriod(
          null!,
          DateTime.UtcNow,
          DateTime.UtcNow + TimeSpan.FromDays(1),
          observation.Elevation,
          DateTime.UtcNow + TimeSpan.FromHours(12),
          groundStation.Location)
      };
    }
  }
}
