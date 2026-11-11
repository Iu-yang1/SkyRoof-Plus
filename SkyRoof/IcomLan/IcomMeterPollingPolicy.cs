namespace SkyRoof
{
  /// <summary>
  /// Meters are low-priority CAT observations, not controls. In satellite
  /// mode only the S-meter is useful while receiving. During transmit,
  /// bias the limited read budget toward PO and ALC; SWR is sampled
  /// less often. Never change the radio's MAIN/SUB selection to poll.
  /// </summary>
  internal static class IcomMeterPollingPolicy
  {
    private static readonly IcomMeterKind[] TransmitSequence =
    [
      IcomMeterKind.Power, IcomMeterKind.Alc,
      IcomMeterKind.Power, IcomMeterKind.Alc,
      IcomMeterKind.Power, IcomMeterKind.Swr
    ];

    internal static IcomMeterKind? Choose(
      IcomDirectLanMeterSettings settings, bool transmitting, int step)
    {
      if (!settings.Enabled) return null;
      if (!transmitting) return settings.S ? IcomMeterKind.S : null;
      var candidates = TransmitSequence.Where(kind => kind switch
      {
        IcomMeterKind.Power => settings.Power,
        IcomMeterKind.Alc => settings.Alc,
        IcomMeterKind.Swr => settings.Swr,
        _ => false
      }).ToArray();
      return candidates.Length == 0 ? null :
        candidates[(step & int.MaxValue) % candidates.Length];
    }

    internal static IcomMeterSnapshot VisibleForMode(
      IcomMeterSnapshot snapshot, bool transmitting) =>
      transmitting
        ? snapshot with { S = null }
        : snapshot with { Power = null, Alc = null, Swr = null };
  }
}
