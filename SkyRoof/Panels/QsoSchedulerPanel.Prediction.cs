using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.Util;
using VE3NEA;

namespace SkyRoof
{
  public partial class QsoSchedulerPanel
  {
    private readonly record struct PredictionRequest(
      SatnogsDbSatellite Satellite, string MySquare, double MyAltitudeMeters,
      string DxSquare, DateTime StartUtc, DateTime EndUtc);

    private readonly record struct PredictionProgress(int Percent, string StageKey);

    internal static bool HasValidRange(DateTime startUtc, DateTime endUtc) =>
      endUtc > startUtc;

    internal readonly record struct UtcWindow(DateTime Start, DateTime End);

    internal static UtcWindow? IntersectVisibility(
      UtcWindow my, UtcWindow dx, UtcWindow range)
    {
      var start = my.Start > dx.Start ? my.Start : dx.Start;
      if (start < range.Start) start = range.Start;
      var end = my.End < dx.End ? my.End : dx.End;
      if (end > range.End) end = range.End;
      return end - start >= TimeSpan.FromSeconds(30) ? new(start, end) : null;
    }


    private static List<OverlapPass> ComputePredictions(PredictionRequest request,
      IProgress<PredictionProgress> progress, CancellationToken cancellation)
    {
      cancellation.ThrowIfCancellationRequested();
      var satellite = request.Satellite;
      var tracker = satellite.Tracker;
      var myStation = MakeStation(request.MySquare, request.MyAltitudeMeters);
      var dxStation = MakeStation(request.DxSquare, 0);

      bool geo = tracker.IsGeoStationary();

      progress.Report(new PredictionProgress(5, "QsoScheduler.ProgressMe"));
      var myPasses = PredictPassesForStation(request, myStation, geo);
      cancellation.ThrowIfCancellationRequested();
      progress.Report(new PredictionProgress(40, "QsoScheduler.ProgressDx"));
      var dxPasses = PredictPassesForStation(request, dxStation, geo);
      cancellation.ThrowIfCancellationRequested();
      progress.Report(new PredictionProgress(70, "QsoScheduler.ProgressOverlap"));

      var overlaps = new List<OverlapPass>();
      int dxFirst = 0;
      for (int i = 0; i < myPasses.Count; i++)
      {
        cancellation.ThrowIfCancellationRequested();
        var my = myPasses[i];
        while (dxFirst < dxPasses.Count &&
               dxPasses[dxFirst].EndTime <= my.StartTime)
          dxFirst++;

        // Sorted intervals: only DX passes intersecting this local pass
        // need examination, rather than the former O(me * DX) scan.
        for (int j = dxFirst; j < dxPasses.Count &&
             dxPasses[j].StartTime < my.EndTime; j++)
        {
          cancellation.ThrowIfCancellationRequested();
          var dx = dxPasses[j];
          var intersection = IntersectVisibility(
            new(my.StartTime, my.EndTime),
            new(dx.StartTime, dx.EndTime),
            new(request.StartUtc, request.EndUtc));
          if (intersection is not { } window) continue;
          var start = window.Start;
          var end = window.End;
          var overlap = new OverlapPass
          {
            Satellite = satellite,
            MyPass = my,
            DxPass = dx,
            MySquare = request.MySquare,
            DxSquare = request.DxSquare,
            CommonStart = start,
            CommonEnd = end,
            Geostationary = geo,
            OrbitNumber = my.OrbitNumber,
            MyMiniPath = ComputeMiniPath(my, start, end, cancellation),
            DxMiniPath = ComputeMiniPath(dx, start, end, cancellation),
            MyMaxElevation = MaxElevationInRange(my, start, end, cancellation),
            DxMaxElevation = MaxElevationInRange(dx, start, end, cancellation)
          };
          overlaps.Add(overlap);
        }

        if (i % 4 == 0 || i == myPasses.Count - 1)
          progress.Report(new PredictionProgress(
            70 + 29 * (i + 1) / Math.Max(1, myPasses.Count),
            "QsoScheduler.ProgressOverlap"));
      }
      cancellation.ThrowIfCancellationRequested();
      progress.Report(new PredictionProgress(100, "QsoScheduler.ProgressOverlap"));
      return overlaps.OrderBy(o => o.CommonStart).ToList();
    }

    private static List<SatellitePass> PredictPassesForStation(
      PredictionRequest request, GroundStation station, bool geostationary)
    {
      var satellite = request.Satellite;
      var tracker = satellite.Tracker;
      var visibility = geostationary
        ? tracker.ComputeGeostationaryPasses(station)
        : tracker.ComputePasses(station, request.StartUtc, request.EndUtc);
      var passes = visibility
        .Select(period => new SatellitePass(station, satellite, period))
        .OrderBy(pass => pass.StartTime).ToList();

      // GEO periods returned by the existing tracker cover a synthetic 24 h.
      // The matching query must instead cover the operator's UTC interval.
      if (geostationary)
        foreach (var pass in passes)
        {
          pass.StartTime = request.StartUtc;
          pass.EndTime = request.EndUtc;
          pass.CulminationTime = request.StartUtc +
            (request.EndUtc - request.StartUtc) / 2;
        }
      return passes;
    }

    private static GroundStation MakeStation(string square, double altitudeMeters)
    {
      var pos = GridSquare.ToGeoPoint(square);
      var location = new GeodeticCoordinate(
        Angle.FromRadians(pos.LatitudeRad),
        Angle.FromRadians(pos.LongitudeRad),
        altitudeMeters / 1000d);
      return new GroundStation(location);
    }

    private const int MiniPathSteps = 10;
    private static PointF[] ComputeMiniPath(SatellitePass pass, DateTime start, DateTime end,
      CancellationToken cancellation)
    {
      var duration = end - start;
      if (duration <= TimeSpan.Zero) return Array.Empty<PointF>();
      var step = duration / MiniPathSteps;
      var path = new PointF[MiniPathSteps + 1];

      for (int i = 0; i <= MiniPathSteps; i++)
      {
        cancellation.ThrowIfCancellationRequested();
        var utc = start + step * i;
        var observation = pass.GetObservationAt(utc);
        if (observation == null) continue;
        double ro = 1 - observation.Elevation.Radians / Utils.HalfPi;
        double phi = Utils.HalfPi - observation.Azimuth.Radians;
        path[i] = new PointF((float)(ro * Math.Cos(phi)), (float)(ro * Math.Sin(phi)));
      }
      return path;
    }

    private static double MaxElevationInRange(SatellitePass pass, DateTime start, DateTime end,
      CancellationToken cancellation)
    {
      if (pass.Geostationary) return pass.MaxElevation;

      const int Steps = 30;
      var duration = end - start;
      if (duration <= TimeSpan.Zero) return 0;
      var step = duration / Steps;
      double max = 0;

      for (int i = 0; i <= Steps; i++)
      {
        cancellation.ThrowIfCancellationRequested();
        var utc = start + step * i;
        var observation = pass.GetObservationAt(utc);
        if (observation == null) continue;
        double el = observation.Elevation.Degrees;
        if (el > max) max = el;
      }
      return max;
    }



  }
}
