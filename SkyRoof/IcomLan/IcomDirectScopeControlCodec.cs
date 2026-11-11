using System.Globalization;
using skycatd;

namespace SkyRoof
{
  /// <summary>
  /// Encodes operator-requested scope writes for the authenticated LAN CI-V
  /// session. Reuses SkyCAT's validated IC-9700 command encoding so the two
  /// transports cannot diverge on BCD, subcommands or fixed-edge limits.
  /// This does not send PTT, frequency or mode CAT commands.
  /// </summary>
  internal static class IcomDirectScopeControlCodec
  {
    internal static byte[] Encode(
      byte radioCivAddress, IcomScopeControlRequest request)
    {
      string receiver = request.Scope switch
      {
        0 => "MAIN",
        1 => "SUB",
        _ => throw new ArgumentOutOfRangeException(nameof(request))
      };
      string[] args = request.Kind switch
      {
        IcomScopeControlKind.SelectedScope =>
          ["U", "SCOPE_SELECT", receiver],
        IcomScopeControlKind.Mode =>
          ["U", "SCOPE_MODE", receiver, request.Mode switch
          {
            IcomScopeMode.Center => "CENTER",
            IcomScopeMode.Fixed => "FIXED",
            IcomScopeMode.ScrollCenter => "SCROLL-C",
            IcomScopeMode.ScrollFixed => "SCROLL-F",
            _ => throw new ArgumentOutOfRangeException(nameof(request))
          }],
        IcomScopeControlKind.Span =>
          ["U", "SCOPE_SPAN", receiver, request.SpanHz.ToString(CultureInfo.InvariantCulture)],
        IcomScopeControlKind.Edge =>
          ["U", "SCOPE_EDGE", receiver, request.EdgeNumber.ToString(CultureInfo.InvariantCulture)],
        IcomScopeControlKind.ReferenceLevel =>
          ["U", "SCOPE_REF", receiver, request.ReferenceDb.ToString("0.0", CultureInfo.InvariantCulture)],
        IcomScopeControlKind.SweepSpeed =>
          ["U", "SCOPE_SPEED", receiver, request.SweepSpeed switch
          {
            IcomScopeSweepSpeed.Fast => "FAST",
            IcomScopeSweepSpeed.Mid => "MID",
            IcomScopeSweepSpeed.Slow => "SLOW",
            _ => throw new ArgumentOutOfRangeException(nameof(request))
          }],
        IcomScopeControlKind.ScopeDuringTx =>
          ["U", "SCOPE_TX", request.Enabled ? "1" : "0"],
        IcomScopeControlKind.CenterType =>
          ["U", "SCOPE_CENTER_TYPE", request.CenterType switch
          {
            IcomScopeCenterType.FilterCenter => "FILTER",
            IcomScopeCenterType.CarrierPoint => "CARRIER",
            IcomScopeCenterType.CarrierPointAbsolute => "ABS",
            _ => throw new ArgumentOutOfRangeException(nameof(request))
          }],
        IcomScopeControlKind.Vbw =>
          ["U", "SCOPE_VBW", receiver, request.Vbw == IcomScopeVbw.Wide ? "WIDE" : "NARROW"],
        IcomScopeControlKind.MarkerPosition =>
          ["U", "SCOPE_MARKER", request.MarkerPosition == IcomScopeMarkerPosition.CarrierPoint ? "CARRIER" : "FILTER"],
        IcomScopeControlKind.FixedEdge =>
          ["U", "SCOPE_FIXED_EDGE",
            request.FrequencyRange.ToString(CultureInfo.InvariantCulture),
            request.EdgeNumber.ToString(CultureInfo.InvariantCulture),
            request.LowerFrequencyHz.ToString(CultureInfo.InvariantCulture),
            request.UpperFrequencyHz.ToString(CultureInfo.InvariantCulture)],
        _ => throw new ArgumentOutOfRangeException(nameof(request))
      };
      (byte subcommand, byte[] payload) = IcomScopeCommands.BuildWrite(args);
      return [0xFE, 0xFE, radioCivAddress, 0xE0, 0x27, subcommand, ..payload, 0xFD];
    }
  }
}
