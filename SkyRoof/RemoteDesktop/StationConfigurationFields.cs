using System.Text.Json.Nodes;

namespace SkyRoof.RemoteDesktop;

// Names are consumed by the current Device/Station/Media/Dsp/Host implementations.
// Existing user-defined keys remain editable in addition to these common fields.
internal static class StationConfigurationFields
{
    internal static string[] ForProcess(string process) => process switch
    {
        "Device" => [
            "SKYROOF_CAT_DIALECT", "SKYROOF_CAT_HOST", "SKYROOF_CAT_PORT",
            "SKYROOF_ICOM_LAN_HOST", "SKYROOF_ICOM_LAN_PORT", "SKYROOF_ICOM_LAN_USERNAME",
            "SKYROOF_ICOM_LAN_PASSWORD", "SKYROOF_ICOM_LAN_RX_RATE", "SKYROOF_ICOM_LAN_RX_CODEC",
            "SKYROOF_ICOM_LAN_TX_RATE", "SKYROOF_ICOM_LAN_RF_SCOPE",
            "SKYROOF_ROTATOR_AZ_MIN", "SKYROOF_ROTATOR_AZ_MAX", "SKYROOF_ROTATOR_EL_MIN", "SKYROOF_ROTATOR_EL_MAX",
            "SKYROOF_DEVICE_PTT_JOURNAL", "SKYROOF_TIMED_TX_DIRECTORY" ],
        "Station" => [
            "SKYROOF_STATION_PIPE_CLIENT_SIDS", "SKYROOF_ORBIT_BACKEND", "SKYROOF_OREKIT_DATA",
            "SKYROOF_OREKIT_EOP_AUTO", "SKYROOF_OREKIT_EOP_SOURCES", "SKYROOF_ISS_OEM_PATH",
            "SKYROOF_ISS_OEM_AUTO", "SKYROOF_ISS_OEM_URL", "SKYROOF_CATALOG_AUTO",
            "SKYROOF_SATNOGS_API", "SKYROOF_CELESTRAK_OMM_URL", "SKYROOF_SATELLITE_CATALOG",
            "SKYROOF_SDR_DEVICE_ARGS", "SKYROOF_SDR_BRIDGE_PATH", "SKYROOF_LOGBOOK_DIRECTORY",
            "SKYROOF_TRACKING_JOB_FILE", "SKYROOF_RECORDING_DIRECTORY" ],
        "Media" => [ "SKYROOF_RX_AUDIO_CHANNELS", "SKYROOF_MEDIA_PIPE_CLIENT_SIDS" ],
        "Dsp" => [
            "SKYROOF_CW_MODEL_DIRECTORY", "SKYROOF_CW_HISTORY_DIRECTORY", "SKYROOF_FT4_RX_DELAY_MS",
            "SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY", "SKYROOF_SATNOGS_TOKEN",
            "SKYROOF_STATION_CALLSIGN", "SKYROOF_STATION_GRID", "SKYROOF_DSP_PIPE_CLIENT_SIDS" ],
        "Host" => [
            "ASPNETCORE_URLS", "SKYROOF_AUTH_DB", "SKYROOF_AUTH_KEYS_DIRECTORY",
            "SKYROOF_BOOTSTRAP_ADMIN_USER", "SKYROOF_BOOTSTRAP_ADMIN_PASSWORD",
            "SKYROOF_RTC_ICE_SERVER", "SKYROOF_WORKSPACE_ORIGINS",
            "SKYROOF_ONVIF_ENDPOINT", "SKYROOF_ONVIF_USERNAME", "SKYROOF_ONVIF_PASSWORD",
            "SKYROOF_FT4_TX_DELAY_MS" ],
        _ => []
    };

    internal static JsonObject ReadEnvironment(JsonObject document, string name)
    {
        var item = document["Processes"]?.AsArray().SingleOrDefault(p => p?["Name"]?.GetValue<string>() == name)?.AsObject()
            ?? throw new InvalidDataException("Missing process: " + name);
        if (item["Environment"] is null) item["Environment"] = new JsonObject();
        return item["Environment"]?.AsObject() ?? throw new InvalidDataException("Invalid process environment.");
    }

    internal static void AddDesktopSid(JsonObject document, string sid)
    {
        var environment = ReadEnvironment(document, "Station");
        const string key = "SKYROOF_STATION_PIPE_CLIENT_SIDS";
        var values = (environment[key]?.GetValue<string>() ?? "").Split(';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        environment[key] = string.Join(';', values.Append(sid).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
