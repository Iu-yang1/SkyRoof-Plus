using System.Text.RegularExpressions;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof.Satellites
{
  internal class SignalParamsResolver
  {
    private const double AudioSampleRate = 48000;

    public static SignalParams? Resolve(SatnogsDbTransmitter tx)
    {
      if (tx == null) return null;

      // Ordered resolution layers, highest priority first. Each layer is a partial GrSatsInfo. manual (the
      // override file) is authoritative; je9pel (mined from the JE9PEL Mode text) fills gaps below the curated
      // satyaml layer; satnogs is the base DB. The value fields below take the first layer that supplies a
      // non-null value (Field); modulation and framing instead give manual priority and then classify the
      // remaining sources as a single string (see ResolveModulation/ResolveFraming).
      // the JE9PEL Mode text is per-satellite, not per-transmitter, so it is ambiguous once a satellite
      // carries more than one transmitter — skip that layer entirely in the multi-transmitter case
      var je9pel = tx.Satellite?.Transmitters.Count > 1 ? null : Je9pelParser.BuildJe9pelLayer(tx);
      var layers = new List<GrSatsInfo?> { tx.manual, tx.gr_sats, je9pel, BuildSatnogsLayer(tx) };

      // Baud: prefer the curated satyaml baudrate, then the SatNOGS DB field,
      // then a token parsed from the description (e.g. "GMSK 9k6")
      double baud = Field(layers, l => l.baudrate) ?? 0;

      Modulation mod = ResolveModulation(tx, je9pel);
      Framing framing = ResolveFraming(tx, je9pel);

      ResolveSpecialSatelliteParams(tx, ref mod, ref framing, ref baud);

      // Independent over-the-air 2026-10-04 UNNE-1B complex IQ fixtures
      // pass CRC at 820 Hz peak deviation for both type-1/type-14 telemetry,
      // whereas 562.5 Hz yields no verified type-1/type-14 frames. Use this
      // specifically validated value instead of the mislabeled DB deviation.
      // A user-authored deviation always wins.
      double? devOverride = mod == Modulation.GMSK ? null :
        IsUnne1b200BaudTelemetry(tx) ? tx.manual?.deviation ?? 820.0 :
        Field(layers, l => l.deviation);
      // AFSK (Bell-202 audio subcarrier over FM, e.g. 1k2 telemetry): the demod's "deviation" is the audio tone
      // half-spacing (1200/2200 → 500 Hz), NOT the RF-FM deviation the DB/je9pel layers report. Pin the Bell-202
      // value (the universal 1200 Bd amateur standard, and gr-satellites' curated value); the audio subcarrier
      // centre (AfCarrier below) is pinned to 1700 Hz the same way.
      if (mod == Modulation.AFSK) devOverride = 500;

      // Differential (PSK only): explicit SatYAML/manual precoding wins.
      // For ordinary BPSK with no precoding metadata, keep null so SkyTlm
      // can trial coherent/DBPSK and lock on a CRC/FEC-valid frame.
      // AO-40 FEC is DBPSK by protocol. For CCSDS this same flag instead
      // selects NRZ-I bit precoding INSIDE the deframer (not RF differential
      // demodulation); preserve the no-precoding default unless declared.
      bool? differential = null;
      if (mod == Modulation.BPSK || mod == Modulation.QPSK)
      {
        string? precoding = Field(layers, l => l.precoding);
        // FUNcube (AO-73/EO-88/JO-97) uses differential BPSK with AO-40
        // FEC. Its SatYAML and SatNOGS records can omit the optional
        // "precoding" key: interpreting absence as explicit coherent BPSK
        // prevents the AO-40 FEC decoder from ever receiving valid symbols.
        // Explicit precoding from a user override remains authoritative.
        differential = precoding != null
          ? precoding.Contains("differential", StringComparison.OrdinalIgnoreCase)
          : framing == Framing.AO40FEC ? true
          : framing == Framing.CCSDS ? false
          : null;
      }

      var sp = new SignalParams(baud, mod, framing, AudioSampleRate, devOverride)
      {
        Manchester = IsManchester(tx.manual?.modulation, tx.gr_sats?.modulation, tx.description),
        Differential = differential,
        RsBasis = Field(layers, l => l.rs_basis),
        FrameSize = Field(layers, l => l.frame_size),
        // Bell-202 audio subcarrier centre; only AFSK consumes it (1700 Hz standard for 1200 Bd telemetry).
        AfCarrier = mod == Modulation.AFSK ? 1700 : null
      };
      // CCSDS carry-through facts (block variant, scrambler, precoding, convolutional, interleaving) are a
      // satyaml concept expressed as structured fields, so they resolve over the structured layers only —
      // manual over gr_sats. The base satnogs/je9pel layers store only raw DB text and must never be scanned
      // for these tokens, so they are excluded here. With no manual override present this is byte-identical
      // to reading gr_sats alone (the P1 safety property).
      return framing == Framing.CCSDS
        ? ApplyCcsdsOptions(sp, new List<GrSatsInfo?> { tx.manual, tx.gr_sats })
        : sp;
    }


    private static void ResolveSpecialSatelliteParams(
      SatnogsDbTransmitter tx, ref Modulation modulation,
      ref Framing framing, ref double baud)
    {
      // AO-73's 145.935 MHz beacon uses the published 1200 Bd DBPSK
      // AO-40 FEC protocol. SatNOGS does not specify that custom framing:
      // without an installed/matched SatYAML entry a valid BPSK transmitter
      // was silently marked "unsupported". Infer the documented protocol
      // only for this exact NORAD + beacon frequency, never its nearby
      // linear transponder or a transmitter with manual framing.
      if (IsAo73BeaconWithoutFraming(tx, framing, modulation))
      {
        framing = Framing.AO40FEC;
        if (baud <= 0) baud = 1200;
      }
      // This downlink carries the same UNNE-1B FSK telemetry when the
      // SatNOGS row says BPSK or omits its baud entirely. The radio's FM
      // transponder is kept as FM, while the panel feeds a separate FSK
      // decoder with these protocol-defined parameters.
      if (IsUnne1b200BaudTelemetry(tx))
      {
        if (ExtractyModulation(tx.manual?.modulation) == Modulation.Unknown)
          modulation = Modulation.FSK;
        if (ExtractFraming(tx.manual?.framing) == Framing.Unknown)
          framing = Framing.HADES;
        if (tx.manual?.baudrate is not > 0) baud = 200;
      }
      else if ((tx.norad_cat_id == Unne1bDeframer.NoradId ||
                tx.Satellite?.norad_cat_id == Unne1bDeframer.NoradId) &&
               modulation == Modulation.FSK)
      {
        if (framing == Framing.Unknown) framing = Framing.HADES;
        if (baud <= 0) baud = 200;
      }

    }

    // Authoritative local protocol key: satellite NORAD plus this exact
    // shared downlink. SatNOGS mode/baud/free text are fallible metadata.
    internal static bool IsUnne1bDownlink(SatnogsDbTransmitter? tx) =>
      tx != null &&
      (tx.norad_cat_id == Unne1bDeframer.NoradId ||
       tx.Satellite?.norad_cat_id == Unne1bDeframer.NoradId) &&
      tx.downlink_low == 436888000L;

    internal static bool IsUnne1b200BaudTelemetry(SatnogsDbTransmitter? tx)
    {
      if (!IsUnne1bDownlink(tx) || IsUnne1bFmRepeater(tx)) return false;
      // Never rewrite actual CW/SSTV transmitters. Accept the 200-baud
      // BPSK row even if its DB baud/description has been left blank, and
      // do not let a wrong gr-satellites modulation veto a known downlink.
      Modulation rawMode = ExtractyModulation($"{tx!.mode} {tx.DownlinkMode}");
      if (rawMode is Modulation.CW or Modulation.SSTV) return false;
      if (rawMode is Modulation.FSK or Modulation.BPSK) return true;
      Modulation descriptionMode = ExtractyModulation(tx.description);
      if (descriptionMode is Modulation.CW or Modulation.SSTV) return false;
      return descriptionMode is Modulation.FSK or Modulation.BPSK ||
             tx.baud == 200 || tx.gr_sats?.baudrate == 200 ||
             Regex.IsMatch(tx.description ?? "", @"\b200\s*(?:bps|baud)\b",
               RegexOptions.IgnoreCase);
    }

    internal static bool IsUnne1bFmRepeater(SatnogsDbTransmitter? tx)
    {
      if (!IsUnne1bDownlink(tx)) return false;
      return string.Equals(tx!.mode, "FM", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tx.DownlinkMode, "FM", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tx.mode, "FMN", StringComparison.OrdinalIgnoreCase) ||
        ((tx.description?.Contains("FM", StringComparison.OrdinalIgnoreCase) ?? false) &&
         (tx.description.Contains("Repeater", StringComparison.OrdinalIgnoreCase) ||
          tx.description.Contains("Voice", StringComparison.OrdinalIgnoreCase)));
    }

    // Used only when an UNNE FM row has no usable database telemetry sibling.
    // It never changes CAT or the selected transmitter's FM classification.
    internal static SignalParams Unne1bFmTelemetryParams() =>
      new(200, Modulation.FSK, Framing.HADES, AudioSampleRate, 820);

    private static bool IsAo73BeaconWithoutFraming(SatnogsDbTransmitter tx,
      Framing framing, Modulation modulation) =>
      framing == Framing.Unknown &&
      tx.manual?.framing == null &&
      (tx.norad_cat_id == 39444 || tx.Satellite?.norad_cat_id == 39444) &&
      tx.downlink_low is long beaconHz &&
      Math.Abs(beaconHz - 145935000L) <= 5000 &&
      modulation == Modulation.BPSK;

    // Wrap today's SatNOGS DB fields into a GrSatsInfo so the base case is just "the lowest layer",
    // keeping Resolve a single uniform loop. The DB carries no deviation/precoding/RS, so those stay null.
    // modulation/framing hold the raw DB free text (not a pre-classified enum name): the field resolver
    // runs the shared extractors on every layer uniformly, and keeping the raw text avoids a lossy enum
    // round-trip (e.g. ExtractFraming("AX100RS") would collapse to AX100ASM).
    private static GrSatsInfo BuildSatnogsLayer(SatnogsDbTransmitter tx)
    {
      string freeText = $"{tx.description} {tx.mode} {tx.DownlinkMode}";
      return new GrSatsInfo
      {
        baudrate = (double?)tx.baud ?? ParseBaud(tx.description),
        modulation = freeText,
        framing = freeText
      };
    }

    // first non-null value of a value-type field across the layers (null layers skipped)
    private static T? Field<T>(IEnumerable<GrSatsInfo?> layers, Func<GrSatsInfo, T?> selector) where T : struct
    {
      foreach (var layer in layers)
        if (layer != null && selector(layer) is T value) return value;
      return null;
    }

    // first non-null value of a reference-type field across the layers (null layers skipped)
    private static string? Field(IEnumerable<GrSatsInfo?> layers, Func<GrSatsInfo, string?> selector)
    {
      foreach (var layer in layers)
        if (layer != null && selector(layer) is string value) return value;
      return null;
    }

    // Modulation: the manual override is authoritative; below it, gr_sats + je9pel + the DB free text are
    // classified as ONE combined string so ExtractyModulation's specificity order (GMSK/GFSK before the
    // generic FSK they contain) wins across sources — a generic gr_sats "FSK" must not shadow a DB "GMSK".
    private static Modulation ResolveModulation(SatnogsDbTransmitter tx, GrSatsInfo? je9pel)
    {
      Modulation manual = ExtractyModulation(tx.manual?.modulation);
      if (manual != Modulation.Unknown) return manual;
      // The actual "Mode V/U - FM Repeater (Voice + (A)FSK)" label
      // contains an FSK substring. Do not reinterpret an explicit FM
      // repeater mode as a dedicated FSK telemetry transmitter.
      if (IsUnne1bFmRepeater(tx)) return Modulation.FM;
      string modeString = $"{tx.gr_sats?.modulation} {je9pel?.modulation} {tx.description} {tx.mode} {tx.DownlinkMode}";
      return ExtractyModulation(modeString);
    }

    // Framing: same discipline as modulation — manual wins, otherwise one combined string over gr_sats +
    // je9pel + the DB free text, classified by ExtractFraming's fixed keyword order.
    private static Framing ResolveFraming(SatnogsDbTransmitter tx, GrSatsInfo? je9pel)
    {
      Framing manual = ExtractFraming(tx.manual?.framing);
      if (manual != Framing.Unknown) return manual;
      string framingString = $"{tx.gr_sats?.framing} {je9pel?.framing} {tx.description} {tx.mode} {tx.DownlinkMode}";
      return ExtractFraming(framingString);
    }

    // Modulations the telemetry pipeline can demodulate. Anything else (CW, SSTV, FM, QPSK) resolves to a
    // valid SignalParams but builds no pipeline.
    private readonly static Modulation[] SupportedModulations = {
      Modulation.FSK,
      Modulation.GFSK,
      Modulation.GMSK,
      Modulation.BPSK,
      Modulation.AFSK
    };

    /// <summary>True if a resolved parameter set is complete enough, and its modulation supported, to build a
    /// telemetry pipeline. One predicate shared by the panel's status ladder and the co-channel sibling ranker
    /// (<see cref="CoChannel.RankedTelemetrySibling"/>), so both agree on what "decodable" means.</summary>
    public static bool IsTelemetryDecodable(SignalParams? signalParams)
    {
      if (signalParams == null) return false;
      if (signalParams.Framing == Framing.Unknown || signalParams.Modulation == Modulation.Unknown || signalParams.Baud == 0) return false;
      return SupportedModulations.Contains(signalParams.Modulation);
    }

    /// <summary>True if the transmitter advertises SSTV anywhere in its mode/description strings. A mixed
    /// transmitter (e.g. UmKA-1 alternating FSK telemetry and SSTV in one pass) classifies as
    /// FSK in <see cref="ExtractyModulation"/>, so the SSTV capability is surfaced separately and the
    /// SSTV decoder runs concurrently with the telemetry pipeline, each self-gating on its own signal.</summary>
    public static bool HasSstv(SatnogsDbTransmitter? tx)
    {
      if (tx == null) return false;
      string s = $"{tx.gr_sats?.modulation} {tx.description} {tx.mode} {tx.DownlinkMode}";
      return s.Contains("SSTV", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if the transmitter advertises SSDV. Unlike every other mode string this is not a
    /// transmitter at all but a payload type carried inside a telemetry stream: HADES-SA's "SSDV" row and
    /// its "FSK 800 bps 1600 Hz separation" row are the same 436.875 MHz signal, and the image packets
    /// arrive interleaved with the telemetry frames. So an SSDV row resolves to
    /// <see cref="Modulation.Unknown"/> and can never build a pipeline on its own — it decodes by pairing
    /// with the co-channel telemetry sibling that actually carries it
    /// (<see cref="CoChannel.RankedTelemetrySibling"/>), exactly as an SSTV selection does.</summary>
    public static bool HasSsdv(SatnogsDbTransmitter? tx)
    {
      if (tx == null) return false;
      string s = $"{tx.gr_sats?.modulation} {tx.description} {tx.mode} {tx.DownlinkMode}";
      return s.Contains("SSDV", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>Apply CCSDS-specific carry-through facts (block variant + satyaml overrides) to the
    /// already-classified <see cref="SignalParams"/>, resolving each fact first-non-null over the structured
    /// layers (manual over gr_sats). Mirrors <c>ParamResolver.ApplyCcsdsOptions</c>.</summary>
    private static SignalParams ApplyCcsdsOptions(SignalParams signalParams, IEnumerable<GrSatsInfo?> structuredLayers)
    {
      string framingText = Field(structuredLayers, l => l.framing) ?? "";
      bool uncoded = framingText.Contains("Uncoded", StringComparison.OrdinalIgnoreCase);
      bool concatenated = framingText.Contains("Concatenated", StringComparison.OrdinalIgnoreCase);
      bool? scrambler = Field(structuredLayers, l => l.scrambler) is string sc ?
        !sc.Equals("none", StringComparison.OrdinalIgnoreCase) : null;
      bool? precoding = Field(structuredLayers, l => l.precoding) is string pc ?
        pc.Contains("differential", StringComparison.OrdinalIgnoreCase) : null;

      return signalParams with
      {
        RsEnabled = !uncoded,
        Convolutional = concatenated ? Field(structuredLayers, l => l.convolutional) ?? "CCSDS" : null,
        RsInterleaving = Field(structuredLayers, l => l.rs_interleaving),
        Scrambler = scrambler,
        Differential = precoding ?? signalParams.Differential
      };
    }

    /// <summary>Parse a baud token: <c>9k6→9600</c>, <c>2k4→2400</c>, plain <c>800/600/300</c>.</summary>
    private static readonly Regex BaudTokenRegex = new(
  @"(?<a>\d{1,2})k(?<b>\d)|(?<plain>\d{3,5})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static double? ParseBaud(string? text)
    {
      if (string.IsNullOrEmpty(text)) return null;
      foreach (Match m in BaudTokenRegex.Matches(text))
      {
        if (m.Groups["a"].Success)
          return int.Parse(m.Groups["a"].Value) * 1000 + int.Parse(m.Groups["b"].Value) * 100;
        if (m.Groups["plain"].Success)
        {
          int v = int.Parse(m.Groups["plain"].Value);
          if (v is 200 or 300 or 600 or 800 or 1200 or 2400 or 4800 or 9600 or 19200 or 38400) return v;
        }
      }
      return null;
    }

    /// <summary>Snap a measured baud rate or deviation to the round value it plainly approximates:
    /// 9600.832 -> 9600. The pipeline reports what it measured, but what the operator reads - and what the
    /// override file records - is a rate, so the digits that carry only measurement noise are dropped. The
    /// step is a tenth of the value's magnitude (100 for a four-digit rate) and the snap is taken only
    /// within 1% of it, so a genuinely odd rate such as 1250 Bd survives unchanged.</summary>
    public static double RoundToStandard(double value)
    {
      if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return value;
      double step = Math.Pow(10, Math.Floor(Math.Log10(value)) - 1);
      double rounded = Math.Round(value / step) * step;
      return Math.Abs(rounded - value) <= 0.01 * value ? rounded : value;
    }

    public static double? RoundToStandard(double? value) => value is double v ? RoundToStandard(v) : null;

    /// <summary>Map one DB <c>mode</c> / <c>description</c> / satyaml string to a deframing flavor.</summary>
    public static Framing ExtractFraming(string? text)
    {
      string s = text ?? "";

      // GOMspace AX100: satyaml framing strings "AX100 ASM+Golay" / "AX100 Reed Solomon", or DB
      // descriptions like "GMSK 4k8 AX.100 Mode 5". Mode 5 = ASM+Golay, Mode 6 = the RS framing; plain
      // "AX100" defaults to ASM+Golay (the overwhelmingly common flavor in the wild). "AX100RS" is the
      // enum's own name — accepted so the round-trip is not lossy (Je9pelParser stores Framing.ToString(),
      // and without this the name collapses to AX100ASM, which is the hazard BuildSatnogsLayer warns about).
      if (s.Contains("AX100", StringComparison.OrdinalIgnoreCase) ||
          s.Contains("AX.100", StringComparison.OrdinalIgnoreCase))
      {
        bool rs = s.Contains("Reed", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("Mode 6", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("AX100RS", StringComparison.OrdinalIgnoreCase);
        return rs ? Framing.AX100RS : Framing.AX100ASM;
      }

      if (s.Contains("CCSDS", StringComparison.OrdinalIgnoreCase))
        return Framing.CCSDS;

      if (s.Contains("USP", StringComparison.OrdinalIgnoreCase))
        return Framing.USP;

      // Geoscan/Sputnix CC1125 framing (satyaml "GEOSCAN", and the DB spells it out as "(Geoscan framing)").
      // MUST precede the AX.25 test: all 14 of these transmitters are described as "AX.25 Beacon and
      // Telemetry (Geoscan framing)", because an AX.25 UI frame rides *inside* the Geoscan frame — so the
      // AX.25 label is true but at the wrong layer, and matching it first would deframe the outer link with
      // the wrong framing and yield nothing.
      if (s.Contains("GEOSCAN", StringComparison.OrdinalIgnoreCase))
        return Framing.GEOSCAN;

      // AO-40 FEC (satyaml "AO-40 FEC"; the DB never names it, so this resolves off the satyaml layer).
      // Only the plain long-frame variant is implemented, so the variants that are NOT are excluded rather
      // than silently mis-deframed: "AO-40 FEC short" (SMOG-P/ATL-1: 52-bit syncword, 51x52 interleaver,
      // RS depth 1) and "AO-40 FEC CRC-16-ARC" (SMOG-1: adds an inner CRC that is kept in the frame, so the
      // frame contents would differ even though the outer chain matches). "AO-40 uncoded" (AO-40, QO-100) is
      // a different deframer entirely and does not contain "FEC", so it falls through on its own.
      // "AO40FEC" is the enum's own name, which Je9pelParser stores as its layer's framing string — accept it
      // so that round-trip is not lossy (compare the BuildSatnogsLayer note about "AX100RS" collapsing).
      if (s.Contains("AO40FEC", StringComparison.OrdinalIgnoreCase) ||
          (s.Contains("AO-40 FEC", StringComparison.OrdinalIgnoreCase) &&
           !s.Contains("AO-40 FEC short", StringComparison.OrdinalIgnoreCase) &&
           !s.Contains("AO-40 FEC CRC-16-ARC", StringComparison.OrdinalIgnoreCase)))
        return Framing.AO40FEC;

      // AX.25, plain or G3RUH-scrambled — one enum value because Ax25G3ruhDeframer runs both chains and
      // FCS-gates them. The dotless "AX25" spelling is accepted too: it is the enum's own name
      // ("AX25G3RUH", closing the same lossy-round-trip hole as the AX100RS and AO40FEC cases) and 11
      // transmitters write it that way in the DB free text ("9k6 FSK AX25", "Mode U - AFSK 1k2 AX25").
      // Safe against the more specific framings that carry AX.25 inside them (GEOSCAN) or name it in their
      // own labels (AX100, USP) — every one of those is tested above and returns first.
      // The exception is a framing satyaml names that we do NOT implement: there the DB's looser "AX25"
      // text must not override it, or an active satellite gets confidently mislabelled and decoded with the
      // wrong chain. Unknown is the honest answer. Only two such names collide today — verified by sweeping
      // the cached DB — but the list is the weak spot here: it grows whenever gr-satellites adds a custom
      // framing whose SatNOGS description also says AX25. The structural fix is layer-aware resolution
      // (satyaml's framing field outranking the DB free text) rather than one combined string.
      if (!s.Contains("ESEO", StringComparison.OrdinalIgnoreCase) &&
          !s.Contains("YUSAT", StringComparison.OrdinalIgnoreCase) &&
          (s.Contains("AX.25", StringComparison.OrdinalIgnoreCase) ||
           s.Contains("AX25", StringComparison.OrdinalIgnoreCase) ||
           s.Contains("G3RUH", StringComparison.OrdinalIgnoreCase)))
        return Framing.AX25G3RUH;

      // AMSAT-EA GENESIS family (HADES-SA SpinnyONE et al.): SatNOGS labels these "GENESIS FSK"/HADES.
      if (s.Contains("GENESIS", StringComparison.OrdinalIgnoreCase) ||
          s.Contains("HADES", StringComparison.OrdinalIgnoreCase))
        return Framing.HADES;

      return Framing.Unknown;
    }

    /// <summary>
    /// Classify modulation from one DB <c>mode</c> / <c>description</c> string. Order matters:
    /// GMSK/GFSK are checked before the generic "FSK" substring they contain. AFSK is its own family
    /// (Bell-202 audio subcarrier over FM) — the demodulator discriminates the RF to audio, then runs the
    /// FSK engine on the recovered tones; keeping it distinct also selects carrier-symmetry CFO.
    /// </summary>
    public static Modulation ExtractyModulation(string? text)
    {
      string s = (text ?? "").ToUpperInvariant();
      if (s.Contains("GMSK")) return Modulation.GMSK;
      if (s.Contains("GFSK")) return Modulation.GFSK;
      if (s.Contains("AFSK")) return Modulation.AFSK;
      if (s.Contains("MSK")) return Modulation.FSK;
      if (s.Contains("FSK")) return Modulation.FSK;

      // PSK constellation order only: any QPSK flavor (incl. OQPSK / DQPSK) → Qpsk, everything else PSK → Bpsk.
      // The differential-vs-coherent distinction is deliberately NOT taken from the label — the DB's
      // "BPSK"/"DBPSK" tags proved unreliable — it comes from the gr_sats precoding field instead
      // (see Resolve), defaulting to coherent.
      if (s.Contains("QPSK")) return Modulation.QPSK;
      if (s.Contains("BPSK")) return Modulation.BPSK;
      if (s.Contains("PSK")) return Modulation.BPSK;

      if (s.Contains("SSTV")) return Modulation.SSTV;
      if (s.Contains("CW")) return Modulation.CW;
      // analog FM last: "FM"/"FMN" are the only mode tokens containing "FM" (OFDM/MFSK/FFSK do not)
      if (s.Contains("FM")) return Modulation.FM;
      return Modulation.Unknown;
    }

    /// <summary>
    /// True if any of the supplied mode/description/modulation strings declares Manchester (bi-phase-L) line
    /// coding — the <c>DBPSK Manchester</c> AMSAT/FUNcube case. Drives <see cref="SignalParams.Manchester"/> so
    /// the demodulator combines chip pairs into data symbols.
    /// </summary>
    public static bool? IsManchester(params string?[] texts)
    {
      foreach (var t in texts)
        if (!string.IsNullOrEmpty(t) && t.Contains("MANCHESTER", StringComparison.OrdinalIgnoreCase)) return true;
      return null;
    }
  }
}
