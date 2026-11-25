namespace SkyRoof.Remote.Host.Realtime;

public sealed record RemoteTrackingRequest
{
    public string TleLine1 { get; init; } = "";
    public string TleLine2 { get; init; } = "";
    public double LatitudeDegrees { get; init; }
    public double LongitudeDegrees { get; init; }
    public double HeightMetres { get; init; }
    public long UtcUnixMilliseconds { get; init; }
    public long DownlinkHz { get; init; }
    public long UplinkHz { get; init; }
    public bool CorrectDownlink { get; init; }
    public bool CorrectUplink { get; init; }
    public bool PointRotator { get; init; }
    public double ElevationMaskDegrees { get; init; }
    public RemoteRadioLink? RadioLink { get; init; }
    public string? CatalogSatelliteId { get; init; }
    public string? CelestialTarget { get; init; }
    public string? EmeDopplerMode { get; init; }
    public RemoteEmePeer? EmePeer { get; init; }
}

public sealed record RemoteEmePeer(double LatitudeDegrees, double LongitudeDegrees, double HeightMetres);
