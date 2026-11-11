using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace SkyRoof
{
  /// <summary>
  /// Best-effort read-only snapshot of IC-9700 network settings.
  /// Missing or malformed fields never overwrite existing configuration.
  /// </summary>
  internal sealed class IcomNetworkReadbackState
  {
    internal string? IpAddress { get; private init; }
    internal int? ControlPort { get; private init; }
    internal int? SerialPort { get; private init; }
    internal int? AudioPort { get; private init; }
    internal bool? NetworkEnabled { get; private init; }
    internal int AvailableCount =>
      (IpAddress != null ? 1 : 0) +
      (ControlPort.HasValue ? 1 : 0) +
      (SerialPort.HasValue ? 1 : 0) +
      (AudioPort.HasValue ? 1 : 0) +
      (NetworkEnabled.HasValue ? 1 : 0);

    internal static IcomNetworkReadbackState Parse(
      IReadOnlyDictionary<string, string> fields)
    {
      string? ip = null;
      if (fields.TryGetValue("IP", out string? address) &&
          IPAddress.TryParse(address, out IPAddress? parsed) &&
          parsed.AddressFamily == AddressFamily.InterNetwork &&
          !parsed.Equals(IPAddress.Any))
        ip = parsed.ToString();

      static int? Port(IReadOnlyDictionary<string, string> fields, string key)
      {
        return fields.TryGetValue(key, out string? text) &&
          int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture,
            out int port) && port is >= 1 and <= 65535
          ? port : null;
      }

      bool? enabled = fields.TryGetValue("ENABLED", out string? status)
        ? status switch { "1" => true, "0" => false, _ => null }
        : null;

      return new IcomNetworkReadbackState
      {
        IpAddress = ip,
        ControlPort = Port(fields, "CONTROL"),
        SerialPort = Port(fields, "SERIAL"),
        AudioPort = Port(fields, "AUDIO"),
        NetworkEnabled = enabled
      };
    }
  }
}
