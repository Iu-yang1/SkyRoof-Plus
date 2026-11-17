using System.Globalization;
using Serilog;

namespace SkyRoof
{
  /// <summary>
  /// Parses the gr-satellites <c>satyaml/</c> files (downloaded and extracted into
  /// <c>Downloads\satyaml\</c>) into <see cref="GrSatsInfo"/> records indexed by NORAD.
  /// Provides the modulation / baudrate / deviation / framing / precoding / RS basis / frame size /
  /// telemetry that the SatNOGS DB does not carry (deviation and precoding in particular).
  /// See <c>docs\satyaml_import_plan.md</c>.
  ///
  /// A tiny line-based parser (no YAML dependency): it reads the scalar transmitter fields and
  /// resolves each transmitter's <c>data:</c> anchor references against the file's top-level
  /// <c>data:</c> map to recover a single telemetry decoder name.
  /// </summary>
  public sealed class SatyamlDb
  {
    private readonly Dictionary<int, List<GrSatsInfo>> byNorad = new();

    private sealed class TransportInfo
    {
      public string? Protocol;
      public readonly List<string> DataRefs = new();
      public readonly List<int> VirtualChannels = new();
    }

    public static SatyamlDb? Load(string dir)
    {
      if (!Directory.Exists(dir)) return null;

      var db = new SatyamlDb();
      foreach (var file in Directory.EnumerateFiles(dir, "*.yml"))
      {
        try { db.ParseFile(file); }
        catch (Exception ex) { Log.Debug(ex, "satyaml: skipped malformed file {File}", file); }
      }
      return db;
    }

    /// <summary>Best transmitter match for a NORAD id, nearest to <paramref name="baud"/>.</summary>
    public GrSatsInfo? Find(int norad, double? baud, long? frequencyHz = null)
    {
      if (!byNorad.TryGetValue(norad, out var entries) || entries.Count == 0) return null;
      // A NORAD ID may have multiple downlinks, including FM and digital.
      // Never attach the sole digital SatYAML row to an unrelated frequency.
      var list = entries;
      if (frequencyHz.HasValue && entries.Any(t => t.frequency_hz.HasValue))
      {
        list = entries.Where(t => t.frequency_hz is long f &&
          Math.Abs(f - frequencyHz.Value) <= 5000).ToList();
        if (list.Count == 0) return null;
      }
      // SatNOGS uses both null and 0 for an unknown baud. Treat neither as
      // a hard 0-Bd modem request: one frequency-matched SatYAML transmitter
      // should still supply its published symbol rate and framing.
      double? knownBaud = baud is double positive && positive > 0 ? positive : null;
      if (list.Count == 1)
        return knownBaud is not double requested || !list[0].baudrate.HasValue ||
          Math.Abs(list[0].baudrate.Value - requested) <= Math.Max(1, requested * 0.02)
          ? list[0] : null;
      if (knownBaud is not double b) return null;

      var nearest = list
        .Where(t => t.baudrate.HasValue)
        .Select(t => new { Info = t, Error = Math.Abs(t.baudrate!.Value - b) })
        .OrderBy(x => x.Error)
        .FirstOrDefault();

      if (nearest == null) return null;

      double tolerance = Math.Max(1.0, Math.Abs(b) * 0.02);
      return nearest.Error <= tolerance ? nearest.Info : null;
    }

    private void ParseFile(string path)
    {
      int? norad = null;
      string? name = null;
      var transmitters = new List<GrSatsInfo>();

      // top-level "data:" map: anchor name -> telemetry decoder string
      var dataAnchors = new Dictionary<string, string>(StringComparer.Ordinal);
      var transports = new Dictionary<string, TransportInfo>(StringComparer.Ordinal);
      string? curAnchor = null, transportAnchor = null, transportList = null;

      bool inTx = false, inData = false, inTransports = false;
      bool inTxDataList = false, inTxTransportList = false, haveTx = false;
      string? curMod = null, curFraming = null, curTxName = null;
      string? curPrecoding = null, curRsBasis = null;
      double? curBaud = null, curDev = null, curFreq = null;
      int? curFrameSize = null;
      string? curConvolutional = null, curScrambler = null;
      int? curRsInterleaving = null;
      var curDataRefs = new List<string>();
      var curTransportRefs = new List<string>();

      void FlushTx()
      {
        if (haveTx)
        {
          TransportInfo? tr = curTransportRefs.Select(r => transports.GetValueOrDefault(r))
            .FirstOrDefault(t => t != null);
          foreach (var r in curTransportRefs)
            if (transports.TryGetValue(r, out var t)) curDataRefs.AddRange(t.DataRefs);
          transmitters.Add(new GrSatsInfo
          {
            modulation = curMod,
            frequency_hz = curFreq.HasValue ? (long)Math.Round(curFreq.Value) : null,
            transport = tr?.Protocol,
            virtual_channels = tr?.VirtualChannels.ToArray(),
            baudrate = curBaud,
            deviation = curDev,
            framing = curFraming,
            precoding = curPrecoding,
            rs_basis = curRsBasis,
            frame_size = curFrameSize,
            convolutional = curConvolutional,
            rs_interleaving = curRsInterleaving,
            scrambler = curScrambler,
            telemetry = ResolveTelemetry(curDataRefs, dataAnchors, name, curTxName)
          });
        }
        curMod = curFraming = curTxName = null;
        curPrecoding = curRsBasis = null;
        curBaud = curDev = curFreq = null;
        curFrameSize = null;
        curConvolutional = curScrambler = null;
        curRsInterleaving = null;
        curDataRefs.Clear();
        curTransportRefs.Clear();
        inTxDataList = inTxTransportList = haveTx = false;
      }

      foreach (var raw in File.ReadLines(path))
      {
        string line = raw.TrimEnd();
        if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;

        int indent = line.Length - line.TrimStart(' ').Length;
        string trimmed = line.Trim();

        if (indent == 0)
        {
          FlushTx();
          inTx = trimmed == "transmitters:";
          inData = trimmed == "data:";
          inTransports = trimmed == "transports:";
          curAnchor = transportAnchor = transportList = null;
          if (trimmed.StartsWith("norad:") && int.TryParse(After(trimmed), out int n)) norad = n;
          else if (trimmed.StartsWith("name:")) name = Unquote(After(trimmed));
          continue;
        }

        // top-level data: section — build anchor -> telemetry decoder map
        if (inData)
        {
          if (indent == 2) curAnchor = ExtractAnchor(trimmed);
          else if (indent >= 4 && curAnchor != null)
          {
            int c = trimmed.IndexOf(':');
            if (c > 0 && trimmed[..c].Trim() == "telemetry")
              dataAnchors[curAnchor] = Unquote(trimmed[(c + 1)..].Trim());
          }
          continue;
        }

        if (inTransports)
        {
          if (indent == 2)
          {
            transportAnchor = ExtractAnchor(trimmed);
            transportList = null;
            if (transportAnchor != null) transports[transportAnchor] = new TransportInfo();
          }
          else if (indent >= 4 && transportAnchor != null &&
            transports.TryGetValue(transportAnchor, out var transport))
          {
            if (trimmed == "data:" || trimmed == "virtual_channels:")
              transportList = trimmed[..^1];
            else if (trimmed.StartsWith("- "))
            {
              string value = trimmed[2..].Trim();
              if (transportList == "data" && value.StartsWith("*"))
                transport.DataRefs.Add(value[1..]);
              else if (transportList == "virtual_channels" && int.TryParse(value, out int vc))
                transport.VirtualChannels.Add(vc);
            }
            else if (trimmed.StartsWith("protocol:"))
              transport.Protocol = Unquote(After(trimmed));
          }
          continue;
        }

        if (!inTx) continue;

        // Transmitter header: 2-space indent, "name:" with no value.
        if (indent == 2 && trimmed.EndsWith(':'))
        {
          FlushTx();
          haveTx = true;
          curTxName = trimmed[..^1].Trim();
          continue;
        }

        if (!haveTx || indent < 4) continue;

        // data: list items ("- *anchor")
        if (trimmed.StartsWith('-'))
        {
          string r = trimmed.TrimStart('-', ' ').TrimStart('*').Trim();
          if (inTxDataList && r.Length > 0) curDataRefs.Add(r);
          if (inTxTransportList && r.Length > 0) curTransportRefs.Add(r);
          continue;
        }

        // Properties: "key: value".
        int colon = trimmed.IndexOf(':');
        if (colon <= 0) continue;
        string key = trimmed[..colon].Trim();
        string val = trimmed[(colon + 1)..].Trim();

        if (key == "data" && val.Length == 0)
        { inTxDataList = true; inTxTransportList = false; continue; }
        if (key == "transports" && val.Length == 0)
        { inTxTransportList = true; inTxDataList = false; continue; }
        inTxDataList = inTxTransportList = false;

        switch (key)
        {
          case "frequency": curFreq = ParseNum(val); break;
          case "modulation": curMod = Unquote(val); break;
          case "framing": curFraming = Unquote(val); break;
          case "baudrate": curBaud = ParseNum(val); break;
          case "deviation": curDev = ParseNum(val); break;
          case "precoding": curPrecoding = Unquote(val); break;
          case "RS basis": curRsBasis = Unquote(val); break;
          case "frame size": curFrameSize = (int?)ParseNum(val); break;
          case "convolutional": curConvolutional = Unquote(val); break;
          case "RS interleaving": curRsInterleaving = (int?)ParseNum(val); break;
          case "scrambler": curScrambler = Unquote(val); break;
        }
      }
      FlushTx();

      if (transmitters.Count == 0 || norad is not int nn) return;
      byNorad[nn] = transmitters;
    }

    /// <summary>
    /// Resolve a transmitter's data references to a single telemetry decoder name.
    /// <c>ax25</c> and <c>csp</c> are framing/transport (not engineering beacons) and are dropped.
    /// Logs a warning and keeps the first if more than one real decoder is found.
    /// </summary>
    private static string? ResolveTelemetry(List<string> refs, Dictionary<string, string> anchors,
      string? satName, string? txName)
    {
      var decoders = refs
        .Select(r => anchors.GetValueOrDefault(r))
        .Where(d => d != null && d != "ax25" && d != "csp")
        .Distinct()
        .ToList();

      if (decoders.Count == 0) return null;
      if (decoders.Count > 1)
        Log.Warning("satyaml: transmitter '{Tx}' of '{Sat}' has {N} telemetry decoders ({List}); using first",
          txName, satName, decoders.Count, string.Join(", ", decoders));
      return decoders[0];
    }

    private static string? ExtractAnchor(string trimmed)
    {
      if (!trimmed.StartsWith('&')) return null;
      int sp = trimmed.IndexOf(' ');
      return sp < 0 ? trimmed[1..].TrimEnd(':') : trimmed[1..sp];
    }

    private static string After(string keyVal) => keyVal[(keyVal.IndexOf(':') + 1)..].Trim();
    private static string Unquote(string s) => s.Trim().Trim('"', '\'');
    private static double? ParseNum(string s) =>
      double.TryParse(Unquote(s), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
  }
}
