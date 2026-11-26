using System.Text.Json;
using VE3NEA;

namespace SkyRoof.RemoteDesktop;

internal enum DesktopStationMode { Standalone, ServerWorkspace }

internal sealed class DesktopStationSettings
{
    public DesktopStationMode Mode { get; set; }
    public string StationId { get; set; } = "alpha";
    public string StationPipe { get; set; } = "skyroof-alpha-station";
    public string HostOrigin { get; set; } = "http://127.0.0.1:8076";
    public string ConfigurationFile { get; set; } = "";
    internal string ServiceName => "SkyRoofRemote-" + StationId;

    internal static string FileName => Path.Combine(Utils.GetUserDataFolder(), "RemoteStation.json");

    internal static DesktopStationSettings Load(string? path = null)
    {
        path ??= FileName;
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<DesktopStationSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Empty remote station profile.");
        settings.Validate();
        return settings;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(Mode) || StationId.Length is < 1 or > 32 ||
            !StationId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ||
            StationPipe.Length is < 1 or > 128 ||
            !StationPipe.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new InvalidDataException("Invalid station ID, pipe or startup mode.");
        if (!Uri.TryCreate(HostOrigin, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)) ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Host origin must be HTTPS or local HTTP, without credentials or path.");
    }

    internal void Save(string? path = null)
    {
        Validate();
        path ??= FileName;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
