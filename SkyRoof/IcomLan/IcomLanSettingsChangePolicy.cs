namespace SkyRoof
{
  internal enum IcomLanSettingsAction
  {
    None = 0,
    Live = 1,
    RestartWindowsEndpoints = 2,
    RenegotiateMedia = 3
  }

  /// <summary>
  /// Only a stream negotiation or the pacing clock requires radio login.
  /// Local microphone/speaker switches reopen WASAPI, while gain/AGC/meter
  /// options can be applied to the running session.
  /// </summary>
  internal static class IcomLanSettingsChangePolicy
  {
    internal static IcomLanSettingsAction Classify(IEnumerable<string> changed)
    {
      IcomLanSettingsAction result = IcomLanSettingsAction.None;
      foreach (string field in changed)
      {
        IcomLanSettingsAction action = Classify(field);
        if (action > result) result = action;
      }
      return result;
    }

    private static IcomLanSettingsAction Classify(string name)
    {
      if (name.StartsWith("SkyRoof.IcomDirectLanMeterSettings.", StringComparison.Ordinal) ||
          name.StartsWith("SkyRoof.IcomLanTxAgcSettings.", StringComparison.Ordinal) ||
          name is "SkyRoof.IcomDirectLanSettings.RetransmitAudio" or
            "SkyRoof.IcomDirectLanTransmitSettings.MicrophoneGain" or
            "SkyRoof.IcomDirectLanTransmitSettings.AutoSelectLanModInput" or
            "SkyRoof.IcomDirectLanReceiveSettings.SpeakerVolume")
        return IcomLanSettingsAction.Live;

      if (name is
          "SkyRoof.IcomDirectLanReceiveSettings.SpeakerDeviceId" or
          "SkyRoof.IcomDirectLanReceiveSettings.PrebufferMs" or
          "SkyRoof.IcomDirectLanTransmitSettings.MicrophoneDeviceId" or
          "SkyRoof.IcomDirectLanTransmitSettings.CaptureBufferMs")
        return IcomLanSettingsAction.RestartWindowsEndpoints;

      if (name.StartsWith("SkyRoof.IcomDirectLanSettings.", StringComparison.Ordinal) ||
          name.StartsWith("SkyRoof.IcomDirectLanReceiveSettings.", StringComparison.Ordinal) ||
          name.StartsWith("SkyRoof.IcomDirectLanTransmitSettings.", StringComparison.Ordinal))
        return IcomLanSettingsAction.RenegotiateMedia;

      return IcomLanSettingsAction.None;
    }
  }
}
