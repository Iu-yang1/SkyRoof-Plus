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
