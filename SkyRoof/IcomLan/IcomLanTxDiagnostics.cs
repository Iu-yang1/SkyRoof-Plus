using System.Text;

namespace SkyRoof
{
  /// <summary>
  /// Operator-facing LAN TX diagnostics. These are independent observations:
  /// local PCM, CI-V MOD register readback and CI-V RF meters. A successful
  /// UDP send is never treated as proof that the radio radiated RF.
  /// </summary>
  internal static class IcomLanTxDiagnostics
  {
    internal static string Format(
      IcomDirectLanTxAudioStatus tx, bool catPtt,
      IcomMeterSnapshot meters, DateTime now)
    {
      var result = new StringBuilder();
      result.AppendLine(SettingsLanguage.Text(
        "IC-9700 LAN 发射诊断（只读）", "IC-9700 LAN TX diagnostics (read-only)"));
      result.AppendLine();
      result.AppendLine($"CAT PTT: {(catPtt ? "ON" : "OFF")} " +
        SettingsLanguage.Text(
          "（软件状态，非电台射频发射确认）",
          "(software state; not radio RF verification)"));
      result.AppendLine($"LAN audio: {(tx.AudioConnected ? "connected" : "offline")}, " +
        $"TX {(tx.TxEnabled ? "enabled" : "disabled")}");
      result.AppendLine($"UDP audio packets sent: {tx.UdpSent}");
      bool recent = tx.LastSentUtcTicks > 0 &&
        now.Ticks >= tx.LastSentUtcTicks &&
        now.Ticks - tx.LastSentUtcTicks < TimeSpan.FromSeconds(2).Ticks;
      result.AppendLine(recent
        ? $"Sent PCM: RMS {tx.LastSentDbfs:F1} dBFS; " +
          $"peak {tx.LastSentPeakDbfs:F1} dBFS"
        : SettingsLanguage.Text(
            "Sent PCM: 最近 2 秒没有确认发出的音频帧",
            "Sent PCM: no confirmed UDP audio frame in the last 2 seconds"));
      result.AppendLine();
      result.AppendLine(SettingsLanguage.Text(
        "电台 CI-V 调制输入读回：",
        "Radio CI-V modulation input readback:"));
      result.AppendLine(string.IsNullOrWhiteSpace(tx.ModSource)
        ? "not read" : tx.ModSource);
      result.AppendLine();
      result.AppendLine(MeterLine("PWR", IcomMeterKind.Power, meters.Power, now));
      result.AppendLine(MeterLine("ALC", IcomMeterKind.Alc, meters.Alc, now));
      result.AppendLine(MeterLine("SWR", IcomMeterKind.Swr, meters.Swr, now));
      result.AppendLine();
      if (tx.MutedLanLevel)
        result.AppendLine(SettingsLanguage.Text(
          "检查：电台 LAN MOD Level 已读回 0%；请到 SET > Connectors > MOD Input 调整。",
          "Check: radio LAN MOD Level read back 0%. Adjust at SET > Connectors > MOD Input."));
      if (tx.WrongModSource)
        result.AppendLine(SettingsLanguage.Text(
          "检查：DATA OFF MOD 或 DATA MOD 至少一项并非 LAN。请核对当前是否处于 DATA 模式。",
          "Check: DATA OFF MOD or DATA MOD is not LAN. Check which DATA mode is active."));
      if (recent && tx.LastSentDbfs <= -22)
        result.AppendLine(SettingsLanguage.Text(
          "提示：发往 LAN 的 PCM RMS 较低；核对峰值及实际 ALC 后再逐步调节输入增益。",
          "Note: transmitted PCM RMS is low. Check peaks and actual ALC before adjusting gain."));
      if (catPtt && meters.Power is { } po && po.Fresh(now) && po.Raw == 0)
        result.AppendLine(SettingsLanguage.Text(
          "检查：CAT PTT 为 ON，但电台 PWR 读回为零；检查电台实际 TX、工作模式及调制电平。",
          "Check: CAT PTT is ON but radio PWR readback is zero. Check actual radio TX, mode and modulation level."));
      result.AppendLine(SettingsLanguage.Text(
        "说明：ALC 对不同发射模式的意义不同；UDP 包数与 MIC 电平都不是 RF 发射证据。",
        "Note: ALC behavior depends on transmit mode; UDP packet counts and MIC level are not RF evidence."));
      return result.ToString();
    }

    private static string MeterLine(
      string label, IcomMeterKind kind, IcomMeterSample? sample, DateTime now)
    {
      if (sample is not { } value || !value.Fresh(now))
        return label + ": " + SettingsLanguage.Text(
          "无新鲜 CI-V 读回", "no fresh CI-V readback");
      double age = (now - value.TimestampUtc).TotalSeconds;
      return $"{label}: {IcomMeterCivCodec.Display(kind, value.Raw)} " +
        $"(CI-V raw {value.Raw:000}; age {age:F1}s)";
    }
  }
}
