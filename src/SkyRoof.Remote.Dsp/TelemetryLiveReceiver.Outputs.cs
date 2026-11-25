using System.Net;

namespace SkyRoof.Remote.Dsp;

public sealed partial class TelemetryLiveReceiver
{
    private static string ArchiveDirectory() =>
        Environment.GetEnvironmentVariable("SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyRoof", "Remote", "TelemetryDecodes", "Binary");

    private void ApplyKiss(int port)
    {
        _kissError = "";
        if (port == 0) { _kiss.Stop(); return; }
        if (_kiss.Active && _kiss.ListeningPort == port) return;
        string bind = Environment.GetEnvironmentVariable("SKYROOF_TELEMETRY_KISS_BIND") ?? "127.0.0.1";
        if (!IPAddress.TryParse(bind, out var address))
        {
            _kiss.Stop();
            _kissError = "Invalid station KISS bind address.";
            return;
        }
        _kiss.Start(port, address);
        if (!_kiss.Active) _kissError = "KISS TCP listener failed to start.";
    }

    private object OutputStatus() => new
    {
        archiveSaved = _archive.Saved, archiveErrors = _archive.Errors,
        kissPort = _kiss.ListeningPort, kissClients = _kiss.ClientCount, kissError = _kissError
    };
}
