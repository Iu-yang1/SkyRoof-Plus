using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

[Collection("BilingualUiAcceptance")]
public sealed class UiLocalizationResourceCoverageTests
{
  private static readonly ResourceManager Resources =
    new("SkyRoof.Localization.UiStrings", typeof(UiLocalizationService).Assembly);

  private static Dictionary<string, string> Read(CultureInfo culture)
  {
    ResourceSet? set = Resources.GetResourceSet(culture, true, false);
    Assert.NotNull(set);
    return set!.Cast<DictionaryEntry>().ToDictionary(
      item => (string)item.Key, item => (string)item.Value!, StringComparer.Ordinal);
  }

  [Fact]
  public void EnglishChineseKeysAndNumericPlaceholdersAreIdentical()
  {
    Dictionary<string, string> en = Read(CultureInfo.InvariantCulture);
    Dictionary<string, string> zh = Read(CultureInfo.GetCultureInfo("zh-CN"));
    Assert.NotEmpty(en);
    Assert.Equal(en.Keys.OrderBy(k => k), zh.Keys.OrderBy(k => k));
    static string[] FormatSlots(string s) => Regex.Matches(s, @"\{(\d+)(?:[^{}]*)\}")
      .Select(m => m.Groups[1].Value).Distinct().OrderBy(v => v).ToArray();
    foreach (string key in en.Keys)
    {
      Assert.False(string.IsNullOrWhiteSpace(en[key]), key);
      Assert.False(string.IsNullOrWhiteSpace(zh[key]), key);
      Assert.Equal(FormatSlots(en[key]), FormatSlots(zh[key]));
    }
  }

  [Fact]
  public void SwitchingUiLanguageDoesNotChangeProtocolCulture()
  {
    CultureInfo savedCulture = CultureInfo.CurrentCulture;
    UiLanguage savedLanguage = SettingsLanguage.Current;
    try
    {
      CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
      UiLocalizationService.Apply(UiLanguage.SimplifiedChinese);
      Assert.Equal("zh-CN", UiLocalizationService.CurrentResourceCulture.Name);
      Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
      Assert.Equal("正在录制I/Q 00:15",
        UiLocalizationService.Format("Recorder.Recording", "I/Q", "00:15"));
      UiLocalizationService.Apply(UiLanguage.English);
      Assert.Equal("en-US", UiLocalizationService.CurrentResourceCulture.Name);
      Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
    }
    finally
    {
      UiLocalizationService.Apply(savedLanguage);
      CultureInfo.CurrentCulture = savedCulture;
    }
  }

  [Fact]
  public void CoreUiAndOperatorStatusKeysArePresent()
  {
    var en = Read(CultureInfo.InvariantCulture);
    foreach (string key in new[]
    {
      "Menu.Language", "Menu.Settings", "Panel.CwConsole", "Panel.Telemetry",
      "Recorder.SourceRawIf", "Telemetry.Ready", "Cw.TxSending",
      "Sstv.TabRx", "Sstv.StatusRxWaiting", "Spectrum.TipReadback",
      "AutoSelection.Toggle", "Dialog.DownloadDialog.Title"
    })
      Assert.Contains(key, en.Keys);
  }
  [Fact]
  public void HamRadioTermsDistinguishTranspondersBeaconsAndPassMatching()
  {
    var zh = Read(CultureInfo.GetCultureInfo("zh-CN"));
    var en = Read(CultureInfo.InvariantCulture);

    Assert.Equal("卫星转发器与信标(&T)", zh["Menu.Transmitters"]);
    Assert.Equal("转发器与信标", zh["Panel.Transmitters"]);
    Assert.Equal("过境匹配", zh["Panel.QsoScheduler"]);
    Assert.Equal("发射机", zh["Control.SatelliteGroupsForm.TransmitterCheckbox"]);
    Assert.Equal("收发信机", zh["Control.SatelliteGroupsForm.TransceiverCheckbox"]);
    Assert.Equal("转发器", zh["Control.SatelliteGroupsForm.TransponderCheckbox"]);
    Assert.Equal("旋转器手动控制", zh["Rotator.Title"]);
    Assert.Equal("州 / 省", zh["Control.QsoEntryPanel.label1"]);
    Assert.Equal("发出 RSV", zh["Sstv.FieldRstTx"]);
    Assert.Equal("收到 RSV", zh["Sstv.FieldRstRx"]);
    Assert.Equal("Satellite", en["Control.QsoSchedulerPanel.label1"]);
    Assert.Equal("卫星", zh["Control.QsoSchedulerPanel.label1"]);
    Assert.Equal("共同可见", zh["QsoScheduler.Now"]);
    Assert.Equal("过境匹配 — {0}  #{1}", zh["QsoSchedule.Title"]);
    Assert.Equal("新增转发器 / 信标…", zh["Transmitter.New"]);
    Assert.Equal("Edit Local Entry...", en["Transmitter.Edit"]);
    foreach (string key in new[] {
      "Recorder.LoadFilter", "Recorder.LoadError", "Telemetry.OverrideSaved",
      "Telemetry.AudioSourceError", "Cw.MessageFull",
      "Spectrum.InvalidSkycatPort", "SatelliteGroups.SaveConfirm"
    })
    {
      Assert.Contains(key, zh.Keys);
      Assert.Contains(key, en.Keys);
    }
    foreach (string key in new[] {
      "Dialog.SstvDenoiseDialog.Title", "SstvDenoise.TitleFor",
      "SstvDenoise.Failed", "Control.SstvDenoiseDialog.NlmGroupBox",
      "Waterfall.CallsignRequired", "Waterfall.CallsignNotSet"
    })
    {
      Assert.Contains(key, zh.Keys);
      Assert.Contains(key, en.Keys);
    }
  }


  [Fact]
  public void MainRadioWorkspacesAndSecondaryDialogsHaveChineseResources()
  {
    var zh = Read(CultureInfo.GetCultureInfo("zh-CN"));
    var en = Read(CultureInfo.InvariantCulture);
    var keys = new Dictionary<string, string>
    {
      ["Control.FrequencyWidget.label3"] = "多普勒",
      ["Control.GainWidget.label1"] = "RF 增益",
      ["Control.RotatorWidget.TrackCheckbox"] = "跟踪",
      ["Control.SatelliteSelectorWidget.label3"] = "载荷",
      ["FrequencyWidget.Terrestrial"] = "地面信号",
      ["FrequencyWidget.StopTx"] = "停止发射",
      ["FrequencyWidget.EnableCtcss"] = "启用 CTCSS 亚音编码",
      ["FrequencyControl.DownlinkBase"] = "下行基准频率",
      ["FrequencyControl.SavedCorrection"] = "已保存的修正量",
      ["FrequencyControl.NoDownlinkTuning"] = "无下行链路，接收调谐不变",
      ["GroupView.Now"] = "过境中",
      ["Column.GroupViewPanel.columnHeader3"] = "下次过境",
      ["Control.SkyViewPanel.RealTimeRadioBtn"] = "实时",
      ["Control.EarthViewPanel.PassRadioBtn"] = "选定过境",
      ["Menu.WaterfallPanel.SelectTransmitterMNU"] = "选择转发器或信标",
      ["AutoSelection.TipTrackDisabled"] = "设置中尚未启用旋转器；启用后才能自动跟踪。",
      ["PttKey.Title"] = "绑定实体 PTT 按键",
      ["AmsatReport.Heard"] = "已收到信号",
      ["AmsatReport.NotHeard"] = "未收到信号",
      ["Onvif.LiveVideo"] = "实时画面",
      ["Onvif.SetDiscover"] = "搜索设备",
      ["Onvif.SetInvalidAddress"] = "ONVIF 地址无效"
    };
    foreach (var (key, expected) in keys)
    {
      Assert.Equal(expected, zh[key]);
      Assert.True(en.ContainsKey(key), $"Missing English resource: {key}");
      Assert.NotEqual(zh[key], en[key]);
    }
  }

}
