using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using SkyRoof;
using SkyRoof.IF;
using VE3NEA;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class SettingsChineseSupplementTests
{
  private static PropertyDescriptor Property(object model, string name) =>
    TypeDescriptor.GetProperties(model).Find(name, false) ??
      throw new InvalidOperationException(model.GetType().Name + "." + name);

  [Fact]
  public void TelemetryInputNamesDescriptionsAndCategoriesSwitchLanguages()
  {
    UiLanguage old = SettingsLanguage.Current;
    try
    {
      var settings = new Settings();
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);

      Assert.Equal("卫星遥测", Property(settings, "Telemetry").DisplayName);
      Assert.Equal("遥测接收输入", Property(settings.Telemetry, "InputSource").DisplayName);
      Assert.Equal("遥测接收输入", Property(settings.Telemetry, "InputSource").Category);
      Assert.Contains("Shared12kIf", Property(settings.Telemetry, "InputSource").Description);
      Assert.Contains("48 kHz", Property(settings.Telemetry, "IfCaptureDeviceId").Description);
      Assert.Contains("仅 AF 输入模式有效", Property(settings.Telemetry, "AfCenterHz").Description);
      Assert.Contains("KISS", Property(settings.Telemetry, "KissServer").Description);
      Assert.Contains("SatNOGS", Property(settings.Telemetry, "SatnogsUploader").Description);
      Assert.Equal("声卡采集 12 kHz IF", Property(settings.Telemetry, "InputSource")
        .Converter.ConvertToString(TelemetryReceiveSource.Capture12kIf));
      Assert.Equal(TelemetryReceiveSource.Shared12kIf, Property(settings.Telemetry, "InputSource")
        .Converter.ConvertFrom(null, CultureInfo.InvariantCulture, "共享 IF→AF 原始 12 kHz IF"));
      Assert.Equal("维纳滤波", Property(settings.Telemetry, "SstvSaveFilter")
        .Converter.ConvertToString(SstvSaveFilter.Wiener));
      Assert.Equal(SstvSaveFilter.Nlm, Property(settings.Telemetry, "SstvSaveFilter")
        .Converter.ConvertFrom(null, CultureInfo.InvariantCulture, "非局部均值滤波"));

      SettingsLanguage.Set(UiLanguage.English);
      Assert.Equal("Telemetry RX Input", Property(settings.Telemetry, "InputSource").DisplayName);
      Assert.Equal("Receive input", Property(settings.Telemetry, "InputSource").Category);
      Assert.Equal("Capture12kIf", Property(settings.Telemetry, "InputSource")
        .Converter.ConvertToString(TelemetryReceiveSource.Capture12kIf));
      Assert.Equal("Wiener", Property(settings.Telemetry, "SstvSaveFilter")
        .Converter.ConvertToString(SstvSaveFilter.Wiener));
    }
    finally { SettingsLanguage.Set(old); }
  }

  [Fact]
  public void CompleteShownAudioTelemetryAndAnnouncementsHaveChineseHelp()
  {
    UiLanguage old = SettingsLanguage.Current;
    try
    {
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      var settings = new Settings();
      foreach (object model in new object[]
      {
        settings.Audio,
        settings.Telemetry,
        settings.OutputStream,
        settings.Announcements,
        settings.Announcements.Announcement1,
        settings.Announcements.Announcement2,
        settings.Announcements.PositionAnnouncement,
        settings.Telemetry.KissServer,
        settings.Telemetry.SatnogsUploader,
        settings.IfAf
      })
      {
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(model))
        {
          if (!property.IsBrowsable) continue;
          Assert.False(string.IsNullOrWhiteSpace(property.Description),
            $"{model.GetType().Name}.{property.Name} missing Chinese help");
          Assert.True(property.Description.Any(ch => ch >= 0x3400 && ch <= 0x9fff),
            $"{model.GetType().Name}.{property.Name} has untranslated help: {property.Description}");
        }
      }
      Assert.Equal("语音播报声卡",
        Property(settings.Announcements, "Soundcard").DisplayName);
      Assert.Equal("距离 AOS 的分钟数",
        Property(settings.Announcements.Announcement1, "Minutes").DisplayName);
      Assert.Contains("{azimuth}",
        Property(settings.Announcements.PositionAnnouncement, "Message").Description);
      Assert.Equal("常规设置",
        Property(settings.Announcements, "Soundcard").Category);
      Assert.Equal("FM 语音处理",
        Property(settings.IfAf, "FmVoiceProcessing").DisplayName);
      Assert.Contains("数字遥测",
        Property(settings.IfAf, "FmVoiceProcessing").Description);
    }
    finally { SettingsLanguage.Set(old); }
  }

  [Fact]
  public void IfAfSettingsUseChineseLabelsHelpValuesAndReadOnlyStatuses()
  {
    UiLanguage previous = SettingsLanguage.Current;
    try
    {
      var config = new IfAfSettings();
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);

      foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(config))
      {
        if (!property.IsBrowsable) continue;
        Assert.Contains(property.DisplayName,
          new[]
          {
            "启用 12 kHz IF→AF 解调", "原始 IF 输入来源",
            "USB 12 kHz IF 采集设备", "IF 采集声道",
            "解调模式", "AF 输出路由", "AF 输出延迟预设",
            "虚拟声卡配对状态", "WSJT-X 虚拟录音输入",
            "虚拟声卡播放输出设备", "SSB 音频带宽", "CW 音频带宽",
            "FM 信道带宽", "SSB 有效下限频率 (Hz)",
            "SSB 有效上限频率 (Hz)", "CW 有效中心频率 (Hz)",
            "CW 有效带宽 (Hz)", "FM 中频有效截止频率 (Hz)",
            "FM 音频有效截止频率 (Hz)", "FM 频偏基准 (Hz)",
            "FM 语音处理", "FM 语音静噪",
            "FM 语音静噪门限 (dBFS)", "SSB 语音陷波",
            "SSB 语音降噪", "SSB 输出微调 (dB)",
            "CW 输出微调 (dB)", "FM 输出增益 (dB)",
            "SSB/CW 慢速自动增益", "SSB/CW AGC 目标均方根电平 (dBFS)",
            "SSB/CW AGC 最大提升 (dB)"
          });
        Assert.True(property.Description.Any(c => c >= 0x3400 && c <= 0x9fff),
          "Untranslated help: " + property.Name);
      }
      Assert.Equal("接收滤波器", Property(config, "SsbBandwidth").Category);
      Assert.Equal("音频增强", Property(config, "FmVoiceProcessing").Category);
      Assert.Equal("左声道", Property(config, "CaptureChannel")
        .Converter.ConvertToString(AudioChannelChoice.Left));
      Assert.Equal(AudioChannelChoice.Right, Property(config, "CaptureChannel")
        .Converter.ConvertFrom("右声道"));
      Assert.Equal("跟随电台模式", Property(config, "Mode")
        .Converter.ConvertToString(IfAfSelection.FollowRadio));
      Assert.Equal("自动识别配对虚拟声卡", Property(config, "AudioRoute")
        .Converter.ConvertToString(IfAfAudioRoute.AutoSignedCable));
      Assert.Equal("已关闭", config.ToString());
      Assert.Contains("录音端点", config.VirtualCaptureDevice);

      SettingsLanguage.Set(UiLanguage.English);
      Assert.Equal("AF audio route", Property(config, "AudioRoute").DisplayName);
      Assert.Equal("Receive filters", Property(config, "SsbBandwidth").Category);
      Assert.Equal("Left", Property(config, "CaptureChannel")
        .Converter.ConvertToString(AudioChannelChoice.Left));
      Assert.Equal("FollowRadio", Property(config, "Mode")
        .Converter.ConvertToString(IfAfSelection.FollowRadio));
      Assert.Equal("Disabled", config.ToString());
    }
    finally { SettingsLanguage.Set(previous); }
  }

  [Fact]
  public void AfcEnglishMetadataNeverFallsBackToChinese()
  {
    UiLanguage previous = SettingsLanguage.Current;
    try
    {
      SettingsLanguage.Set(UiLanguage.English);
      var afc = new AfcSettings();
      foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(afc))
      {
        if (!property.IsBrowsable) continue;
        Assert.DoesNotContain(property.DisplayName,
          new[] { "捕获策略（速度 / 灵敏度）", "信号丢失后的 AFC 修正策略",
            "IF 录音声道" });
        Assert.DoesNotContain(property.Description,
          new[] { "选择 IC-9700 USB IF 实际所在声道。",
            "HoldLast（推荐）" });
        Assert.True(property.Description.All(c => c < 0x3400 || c > 0x9fff),
          "Unexpected Chinese AFC description: " + property.Name);
      }
      Assert.Equal("Acquisition strategy (speed / sensitivity)",
        Property(afc, "AcquisitionPreset").DisplayName);
      Assert.Contains("HoldLast",
        Property(afc, "SignalLossPolicy").Description);
      Assert.Equal("Adaptive", Property(afc, "AcquisitionPreset")
        .Converter.ConvertToString(SkyRoof.AFC.AfcAcquisitionPreset.Adaptive));
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      Assert.Equal("自适应", Property(afc, "AcquisitionPreset")
        .Converter.ConvertToString(SkyRoof.AFC.AfcAcquisitionPreset.Adaptive));
    }
    finally { SettingsLanguage.Set(previous); }
  }

  [Fact]
  public void BooleanValuesAreLocalizedAndStillRoundTrip()
  {
    UiLanguage previous = SettingsLanguage.Current;
    try
    {
      var settings = new Settings();
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      var value = Property(settings.Telemetry, "ArchiveToFile").Converter;
      Assert.Equal("是", value.ConvertToString(true));
      Assert.Equal("否", value.ConvertToString(false));
      Assert.Equal(true, value.ConvertFrom("是"));
      Assert.Equal(false, value.ConvertFrom("否"));
      Assert.Equal(true, value.ConvertFrom("True"));
      SettingsLanguage.Set(UiLanguage.English);
      Assert.Equal("True", value.ConvertToString(true));
      Assert.Equal("False", value.ConvertToString(false));
      Assert.Equal(false, value.ConvertFrom("False"));
      settings.Telemetry.ArchiveToFile = true;
      Assert.Contains("\"ArchiveToFile\":true", JsonConvert.SerializeObject(settings));
    }
    finally { SettingsLanguage.Set(previous); }
  }

  [Fact]
  public void SettingLanguageChangesOnlyPresentationNotJsonOrEnumOrdinal()
  {
    UiLanguage old = SettingsLanguage.Current;
    try
    {
      var settings = new Settings();
      settings.Telemetry.InputSource = TelemetryReceiveSource.Shared12kIf;
      settings.Telemetry.AfCenterHz = 1900;
      settings.Audio.RecorderSource = RecordingAudioSource.IfAfDemodulated;
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      var selection = Property(settings.Audio, "RecorderSource");
      Assert.Equal("IF→AF 解调后 PCM", selection.Converter
        .ConvertToString(settings.Audio.RecorderSource));
      Assert.Equal(RecordingAudioSource.IfAfDemodulated, selection.Converter
        .ConvertFrom("IF→AF 解调后 PCM"));
      string json = JsonConvert.SerializeObject(settings);
      Assert.Contains("\"InputSource\":1", json);
      Assert.Contains("\"AfCenterHz\":1900", json);
      Assert.Contains("\"RecorderSource\":4", json);
      Assert.DoesNotContain("共享 IF→AF", json);
      var restored = JsonConvert.DeserializeObject<Settings>(json)!;
      Assert.Equal(settings.Telemetry.InputSource, restored.Telemetry.InputSource);
      Assert.Equal(settings.Audio.RecorderSource, restored.Audio.RecorderSource);
      Assert.Equal(1900, restored.Telemetry.AfCenterHz);
    }
    finally { SettingsLanguage.Set(old); }
  }
}
