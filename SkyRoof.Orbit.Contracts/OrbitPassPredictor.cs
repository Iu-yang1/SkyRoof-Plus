namespace SkyRoof.Orbit.Contracts;

/// <summary>
/// Backend-independent geometric pass search. Uses a fixed bracket step and
/// bisection to 1 second for horizon crossings. Work remains within the
/// supplied start/end and a bounded 72-hour back/forward recovery range.
/// </summary>
public static class OrbitPassPredictor
{
    public static IReadOnlyList<OrbitPass> FindPasses(
        IOrbitTrack track,
        GroundStationSpec station,
        DateTimeOffset start,
        DateTimeOffset end,
        double elevationMaskDegrees = 0,
        TimeSpan? sampleStep = null)
    {
        return FindPasses(
            utc => track.Observe(station, utc).ElevationDegrees,
            start, end, elevationMaskDegrees, sampleStep);
    }

    // Function form permits deterministic analytic tests independent of Orekit, IKVM and EOP.
    public static IReadOnlyList<OrbitPass> FindPasses(
        Func<DateTimeOffset, double> elevationDegrees,
        DateTimeOffset start,
        DateTimeOffset end,
        double elevationMaskDegrees = 0,
        TimeSpan? sampleStep = null)
    {
        if (end <= start)
            throw new ArgumentException("Pass search end must be after start.", nameof(end));

        TimeSpan step = sampleStep ?? TimeSpan.FromSeconds(30);
        if (step <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sampleStep));

        const int recoveryHours = 72;
        DateTimeOffset startUtc = start.ToUniversalTime();
        DateTimeOffset endUtc = end.ToUniversalTime();
        var result = new List<OrbitPass>();
        double prevElevation = elevationDegrees(startUtc) - elevationMaskDegrees;
        bool inPass = prevElevation >= 0;
        bool actualAos = false;
        DateTimeOffset aos = startUtc;

        if (inPass)
        {
            // AOS must be recovered across the left edge, never silently relabelled start.
            DateTimeOffset upper = startUtc;
            for (DateTimeOffset candidate = startUtc - step;
                 candidate >= startUtc.AddHours(-recoveryHours);
                 candidate -= step)
            {
                if (elevationDegrees(candidate) < elevationMaskDegrees)
                {
                    aos = RefineCrossing(elevationDegrees, candidate, upper, elevationMaskDegrees, rising: true);
                    actualAos = true;
                    break;
                }
                upper = candidate;
            }
        }

        DateTimeOffset prevTime = startUtc;
        while (prevTime < endUtc)
        {
            DateTimeOffset next = prevTime + step;
            if (next > endUtc)
                next = endUtc;
            double elevation = elevationDegrees(next) - elevationMaskDegrees;

            if (!inPass && prevElevation < 0 && elevation >= 0)
            {
                aos = RefineCrossing(elevationDegrees, prevTime, next, elevationMaskDegrees, rising: true);
                actualAos = true;
                inPass = true;
            }
            else if (inPass && prevElevation >= 0 && elevation < 0)
            {
                var los = RefineCrossing(elevationDegrees, prevTime, next, elevationMaskDegrees, rising: false);
                result.Add(BuildPass(elevationDegrees, aos, los, actualAos, true, step));
                inPass = false;
                actualAos = false;
            }

            prevTime = next;
            prevElevation = elevation;
        }

        if (inPass)
        {
            DateTimeOffset lower = endUtc;
            DateTimeOffset los = endUtc;
            bool actualLos = false;
            for (DateTimeOffset next = endUtc + step;
                 next <= endUtc.AddHours(recoveryHours);
                 next += step)
            {
                if (elevationDegrees(next) < elevationMaskDegrees)
                {
                    los = RefineCrossing(elevationDegrees, lower, next, elevationMaskDegrees, rising: false);
                    actualLos = true;
                    break;
                }
                lower = next;
            }
            result.Add(BuildPass(elevationDegrees, aos, los, actualAos, actualLos, step));
        }

        return result;
    }

    private static OrbitPass BuildPass(
        Func<DateTimeOffset, double> elevation,
        DateTimeOffset aos, DateTimeOffset los,
        bool actualAos, bool actualLos, TimeSpan step)
    {
        DateTimeOffset maxTime = aos;
        double maxElevation = double.NegativeInfinity;
        for (DateTimeOffset t = aos; t <= los; t += step)
        {
            double value = elevation(t);
            if (value > maxElevation)
            {
                maxElevation = value;
                maxTime = t;
            }
        }
        double endElevation = elevation(los);
        if (endElevation > maxElevation)
        {
            maxElevation = endElevation;
            maxTime = los;
        }

        DateTimeOffset left = maxTime - step;
        DateTimeOffset right = maxTime + step;
        if (left < aos) left = aos;
        if (right > los) right = los;

        // Golden-section maximization of the local elevation peak.
        const double ratio = 0.3819660112501051;
        while ((right - left).TotalSeconds > 1)
        {
            var width = right - left;
            DateTimeOffset t1 = left.AddTicks((long)(width.Ticks * ratio));
            DateTimeOffset t2 = right.AddTicks(-(long)(width.Ticks * ratio));
            if (elevation(t1) <= elevation(t2))
                left = t1;
            else
                right = t2;
        }
        DateTimeOffset peak = left.AddTicks((right - left).Ticks / 2);
        return new OrbitPass(aos, los, peak, elevation(peak), actualAos, actualLos);
    }

    private static DateTimeOffset RefineCrossing(
        Func<DateTimeOffset, double> elevation,
        DateTimeOffset lower, DateTimeOffset upper,
        double mask, bool rising)
    {
        while ((upper - lower).TotalSeconds > 1)
        {
            DateTimeOffset middle = lower.AddTicks((upper - lower).Ticks / 2);
            bool above = elevation(middle) >= mask;
            if (above == rising)
                upper = middle;
            else
                lower = middle;
        }
        return upper;
    }
}
