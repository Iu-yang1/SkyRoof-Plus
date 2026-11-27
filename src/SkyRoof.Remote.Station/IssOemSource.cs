using SkyRoof.Orbit.Contracts;
using SkyRoof.Orbit.Orekit;

namespace SkyRoof.Remote.Station;

/// <summary>Reloads an operator-provided ISS OEM on replacement; preserves TLE as explicit fallback.</summary>
internal sealed class IssOemSource
{
    private readonly string path = IssOemRefreshService.CachePath;
    private (DateTime Modified, long Length)? version;
    private OrekitIssOemTrack? track;
    private string state = "ISS_OEM_NOT_CONFIGURED";

    internal (IOrbitTrack Track, string Backend, string QualityPrefix) Select(
        IOrbitBackend backend, IOrbitTrack tle, DateTimeOffset utc)
    {
        if (tle.NoradNumber != 25544 || backend is not OrekitOrbitBackend)
            return (tle, backend.BackendId, "");
        Reload(path);
        if (track is not null && track.Covers(utc))
            return (track, "orekit-ikvm-oem", state == "ISS_OEM_READY" ? "" : state + ";");
        if (track is not null)
            return (tle, backend.BackendId, "ISS_OEM_OUTSIDE_COVERAGE;TLE_FALLBACK;");
        return (tle, backend.BackendId, state + ";TLE_FALLBACK;");
    }

    private void Reload(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists)
            {
                track = null;
                version = null;
                state = "ISS_OEM_MISSING";
                return;
            }
            var current = (info.LastWriteTimeUtc, info.Length);
            if (version == current) return;
            version = current;
            var candidate = new OrekitIssOemTrack(info.FullName);
            track = candidate;
            state = "ISS_OEM_READY";
        }
        catch (Exception ex)
        {
            state = track is null ? "ISS_OEM_INVALID" : "ISS_OEM_RETAINED_AFTER_INVALID_UPDATE";
            // Do not leak the configured station file path to remote clients.
            System.Diagnostics.Trace.TraceWarning("ISS OEM reload failed: {0}", ex.GetType().Name);
        }
    }
}
