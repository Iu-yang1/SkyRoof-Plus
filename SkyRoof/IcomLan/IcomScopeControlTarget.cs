namespace SkyRoof
{
  /// <summary>
  /// Resolve the scope receiver only from an explicit UI choice, a received
  /// waveform, or a confirmed CI-V SELECT readback. Never guess MAIN from
  /// ParsePartial's neutral placeholder when SELECT was not queried.
  /// </summary>
  internal static class IcomScopeControlTarget
  {
    internal static bool TryResolve(
      IcomLanScopeBand selectedBand,
      IcomScopeFrame? latestFrame,
      IcomScopeReadbackState? lastReadback,
      out byte receiver)
    {
      switch (selectedBand)
      {
        case IcomLanScopeBand.Main:
          receiver = 0;
          return true;
        case IcomLanScopeBand.Sub:
          receiver = 1;
          return true;
      }

      if (latestFrame != null && latestFrame.Scope <= 1)
      {
        receiver = latestFrame.Scope;
        return true;
      }

      if (lastReadback != null && lastReadback.HasField("SELECT"))
      {
        receiver = lastReadback.SelectedScope;
        return true;
      }

      receiver = 0;
      return false;
    }
  }
}
