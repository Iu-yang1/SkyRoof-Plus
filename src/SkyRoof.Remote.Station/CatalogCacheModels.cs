using System.Text.Json;
using SGPdotNET.Parsers;
using SkyRoof.Orbit.Contracts;
using System.Text.Json.Serialization;

namespace SkyRoof.Remote.Station;

// The desktop cache contains many UI/enrichment fields. This projection
// consumes only its documented serialized orbit/radio fields.
internal sealed record CatalogCacheSatellite
{
    [JsonPropertyName("sat_id")] public string SatelliteId { get; init; } = "";
    [JsonPropertyName("norad_cat_id")] public int? Norad { get; init; }
    public string Name { get; init; } = "";
    public string? Names { get; init; }
    public string Status { get; init; } = "";
    public string[]? AllNames { get; init; }
    public CatalogCacheTle? Tle { get; init; }
    public CatalogCacheTle? AutomaticTle { get; init; }
    public CatalogCacheTle? ManualTle { get; init; }
    public DateTimeOffset? ManualTleExpiresUtc { get; init; }
    public CatalogCacheTransmitter[] Transmitters { get; init; } = [];

    internal CatalogCacheTle? OrbitAt(DateTimeOffset utc) =>
        ManualTle is not null && ManualTleExpiresUtc > utc ? ManualTle :
        AutomaticTle ?? (ManualTle is null ? Tle : null);
}

internal sealed record CatalogCacheTle
{
    [JsonPropertyName("sat_id")] public string? SatelliteId { get; init; }
    [JsonPropertyName("norad_cat_id")] public int? Norad { get; init; }
    public DateTimeOffset? Updated { get; init; }
    public string Tle1 { get; init; } = "";
    public string Tle2 { get; init; } = "";
    [JsonPropertyName("tle_source")] public string Source { get; init; } = "";
    public JsonElement? Omm { get; init; }

    internal bool HasTle => Tle1.StartsWith("1 ", StringComparison.Ordinal) &&
        Tle2.StartsWith("2 ", StringComparison.Ordinal);

    internal OrbitMeanElements? ReadOmm()
    {
        if (Omm is not { ValueKind: JsonValueKind.Object } json) return null;
        // Match the original desktop's Newtonsoft serialization of OmmData.
        var omm = Newtonsoft.Json.JsonConvert.DeserializeObject<OmmData>(json.GetRawText())
            ?? throw new InvalidDataException("Invalid cached OMM.");
        if (omm.NoradCatID == 0 || omm.MeanMotion <= 0 || omm.Eccentricity is < 0 or >= 1 ||
            omm.Epoch == default)
            throw new InvalidDataException("Incomplete cached OMM mean elements.");
        return new OrbitMeanElements(checked((int)omm.NoradCatID), omm.ObjectID ?? "",
            string.IsNullOrWhiteSpace(omm.ClassificationType) ? 'U' : omm.ClassificationType[0],
            new DateTimeOffset(DateTime.SpecifyKind(omm.Epoch, DateTimeKind.Utc)),
            omm.MeanMotion, omm.MeanMotionDot, omm.MeanMotionDDot, omm.Eccentricity,
            omm.Inclination, omm.RAOfAscNode, omm.ArgOfPericenter, omm.MeanAnomaly,
            omm.BStar, omm.EphemerisType, checked((int)omm.ElementSetNo),
            checked((int)omm.RevAtEpoch));
    }
}

internal sealed record CatalogCacheTransmitter
{
    [JsonPropertyName("sat_id")] public string? SatelliteId { get; init; }
    [JsonPropertyName("norad_cat_id")] public int? Norad { get; init; }
    [JsonPropertyName("local_custom")] public bool LocalCustom { get; init; }
    public JsonElement? Manual { get; init; }
    [JsonPropertyName("gr_sats")] public JsonElement? GrSats { get; init; }
    public JsonElement? Baud { get; init; }
    [JsonPropertyName("uuid")] public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Alive { get; init; }
    public string? Mode { get; init; }
    public string? DownlinkMode { get; init; }
    [JsonPropertyName("uplink_mode")] public string? UplinkMode { get; init; }
    public string? Status { get; init; }
    [JsonPropertyName("downlink_low")] public long? DownlinkLow { get; init; }
    [JsonPropertyName("downlink_high")] public long? DownlinkHigh { get; init; }
    [JsonPropertyName("uplink_low")] public long? UplinkLow { get; init; }
    [JsonPropertyName("uplink_high")] public long? UplinkHigh { get; init; }
    public bool Invert { get; init; }
}
