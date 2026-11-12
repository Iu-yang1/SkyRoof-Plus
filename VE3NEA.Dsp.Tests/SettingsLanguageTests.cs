using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class SettingsLanguageTests
{
  [Fact]
  public void SwitchingLanguagePreservesPropertyIdentityAndSettingsSerialization()
  {
    var previous = SettingsLanguage.Current;
    try
    {
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      var settings = new Settings();
      var zh = TypeDescriptor.GetProperties(settings).Find("Afc", false);
      Assert.NotNull(zh);
      Assert.Contains("自动频率", zh!.DisplayName);
      Assert.Equal("Afc", zh.Name);
      var afcZh = TypeDescriptor.GetProperties(settings.Afc).Find("InputDeviceId", false);
      Assert.NotNull(afcZh);
      Assert.Contains("AFC 音频输入", afcZh!.DisplayName);

      SettingsLanguage.Set(UiLanguage.English);
      var en = TypeDescriptor.GetProperties(settings).Find("Afc", false);
      Assert.NotNull(en);
      Assert.Contains("FM / CW / SSB AFC", en!.DisplayName);
      var afcEn = TypeDescriptor.GetProperties(settings.Afc).Find("InputDeviceId", false);
      Assert.NotNull(afcEn);
      Assert.Equal("AFC audio input (12 kHz IF)", afcEn!.DisplayName);

      var scope = TypeDescriptor.GetProperties(settings.IcomLanSpectrum)
        .Find("ScopeReferenceLevelDb", false);
      Assert.NotNull(scope);
      scope!.SetValue(settings.IcomLanSpectrum, 3.5);
      Assert.Equal(3.5, settings.IcomLanSpectrum.ScopeReferenceLevelDb);

      settings.Ui.Language = UiLanguage.English;
      string json = JsonConvert.SerializeObject(settings);
      Assert.Contains("\"ScopeReferenceLevelDb\":3.5", json.Replace(" ", ""));
      Assert.Contains("\"Language\":0", json.Replace(" ", ""));
      var restored = JsonConvert.DeserializeObject<Settings>(json)!;
      Assert.Equal(UiLanguage.English, restored.Ui.Language);
      Assert.Equal(3.5, restored.IcomLanSpectrum.ScopeReferenceLevelDb);
    }
    finally { SettingsLanguage.Set(previous); }
  }

  [Fact]
  public void RecentIcomLanOptionsHaveLocalizedNamesDescriptionsAndSummaries()
  {
    var previous = SettingsLanguage.Current;
    try
    {
      var settings = new Settings();
      var tx = settings.IcomDirectLan.Transmit;
      var rx = settings.IcomDirectLan.Receive;
      var agc = tx.Agc;
      var spectrum = settings.IcomLanSpectrum;

      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      static void Check(object model, string property, string name, string help)
      {
        PropertyDescriptor item = TypeDescriptor.GetProperties(model)
          .Find(property, false)!;
        Assert.NotNull(item);
        Assert.Equal(name, item.DisplayName);
        Assert.Contains(help, item.Description);
      }

      Check(tx, nameof(tx.AutoSelectLanModInput),
        "自动选择电台 LAN 调制输入", "CAT PTT");
      Check(tx, nameof(tx.PrebufferMs), "发射网络预缓冲 (ms)", "IC-9700");
      Check(tx, nameof(tx.CaptureBufferMs), "发射麦克风采集缓冲 (ms)", "WASAPI");
      Check(tx, nameof(tx.JitterPrefillMs), "发射软件抖动预填充 (ms)", "20 ms");
      Check(tx, nameof(tx.JitterMaximumMs), "发射软件抖动缓冲上限 (ms)", "丢弃旧采样");
      Check(tx, nameof(tx.MicrophoneGain), "麦克风增益 (%)", "0–200%");
      Check(tx, nameof(tx.Agc), "发射语音 AGC", "FT4/FT8/Q65");
      Check(rx, nameof(rx.PrebufferMs), "接收扬声器播放缓冲 (ms)", "Windows");
      Check(agc, nameof(agc.Enabled), "启用语音 AGC", "默认关闭");
      Check(agc, nameof(agc.TargetRmsDbfs), "目标均方根电平 (dBFS)", "−18 dBFS");
      Check(agc, nameof(agc.MaximumGainDb), "最大补偿增益 (dB)", "+12 dB");
      Check(agc, nameof(agc.NoiseFloorDbfs), "噪声门限 (dBFS)", "−48 dBFS");
      Check(agc, nameof(agc.AttackMs), "AGC 启动时间 (ms)", "40 ms");
      Check(agc, nameof(agc.ReleaseMs), "AGC 恢复时间 (ms)", "450 ms");
      Check(agc, nameof(agc.PeakCeilingDbfs), "峰值上限 (dBFS)", "−3 dBFS");
      Check(spectrum, nameof(spectrum.SkyCatControlPort),
        "SkyCAT 频谱控制 CAT TCP 端口", "4532");
      Check(spectrum, nameof(spectrum.DirectLanFastSweep),
        "原生 LAN 快速频谱扫描", "CI-V");
      Assert.Contains("发射音频已启用", tx.ToString());
      Assert.Contains("语音 AGC 已关闭", agc.ToString());
      Assert.Contains("接收音频已启用", rx.ToString());

      SettingsLanguage.Set(UiLanguage.English);
      Check(tx, nameof(tx.AutoSelectLanModInput),
        "Auto select LAN MOD input", "recent PC TX PCM");
      Check(tx, nameof(tx.JitterPrefillMs),
        "TX software jitter prefill (ms)", "20-ms TX clock");
      Check(agc, nameof(agc.TargetRmsDbfs),
        "Target RMS (dBFS)", "voice RMS");
      Check(spectrum, nameof(spectrum.SkyCatControlPort),
        "SkyCAT CAT control TCP port", "4532");
      Assert.Equal("TX enabled", tx.ToString());
      Assert.Equal("Voice AGC off", agc.ToString());
      Assert.Equal("RX enabled", rx.ToString());

      // Localized metadata must not rename JSON keys or change defaults.
      string json = JsonConvert.SerializeObject(settings.IcomDirectLan);
      Assert.Contains("\\"AutoSelectLanModInput\\":true", json);
      Assert.Contains("\\"CaptureBufferMs\\":150", json);
      Assert.Contains("\\"JitterPrefillMs\\":60", json);
      Assert.Contains("\\"TargetRmsDbfs\\":-18", json);
      Assert.DoesNotContain("自动选择电台", json);
    }
    finally { SettingsLanguage.Set(previous); }
  }

  [Fact]
  public void WinFormsPropertyGridCanSwitchLanguageWithoutReplacingSettingsModel()
  {
    Exception? error = null;
    var thread = new Thread(() =>
    {
      var previous = SettingsLanguage.Current;
      try
      {
        SettingsLanguage.Set(UiLanguage.English);
        var edited = new Settings();
        using var grid = new PropertyGrid { SelectedObject = edited };
        var original = grid.SelectedObject;
        Assert.Same(edited, original);
        Assert.Equal("Scope reference level",
          TypeDescriptor.GetProperties(edited.IcomLanSpectrum)
            .Find("ScopeReferenceLevelDb", false)!.DisplayName);

        SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
        grid.SelectedObject = null;
        grid.SelectedObject = edited;
        Assert.Same(original, grid.SelectedObject);
        Assert.Equal("频谱参考电平",
          TypeDescriptor.GetProperties(edited.IcomLanSpectrum)
            .Find("ScopeReferenceLevelDb", false)!.DisplayName);
      }
      catch (Exception ex) { error = ex; }
      finally { SettingsLanguage.Set(previous); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(12)));
    if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
  }

  [Theory]
  [InlineData(UiLanguage.English, "User", "Audio", "OutputStream")]
  [InlineData(UiLanguage.SimplifiedChinese, "用户信息", "音频", "音频输出流")]
  public void CoreSettingsSectionsAreBrowsableAndKeepTheirSerializedNames(
    UiLanguage language, string userLabel, string audioLabel, string outputLabel)
  {
    var previous = SettingsLanguage.Current;
    try
    {
      SettingsLanguage.Set(language);
      var settings = new Settings();
      var properties = TypeDescriptor.GetProperties(settings,
        new Attribute[] { BrowsableAttribute.Yes });
      var user = properties.Find("User", false);
      var audio = properties.Find("Audio", false);
      var output = properties.Find("OutputStream", false);
      Assert.NotNull(user);
      Assert.NotNull(audio);
      Assert.NotNull(output);
      Assert.Equal(userLabel, user!.DisplayName);
      Assert.Equal(audioLabel, audio!.DisplayName);
      Assert.Equal(outputLabel, output!.DisplayName);
      Assert.True(user.IsBrowsable);
      Assert.True(audio.IsBrowsable);
      Assert.True(output.IsBrowsable);
      Assert.True(user.Converter.GetPropertiesSupported());
      Assert.True(audio.Converter.GetPropertiesSupported());
      Assert.True(output.Converter.GetPropertiesSupported());

      // UI presentation metadata may not change persistent JSON keys.
      settings.User.Call = "N0CALL";
      settings.OutputStream.Enabled = true;
      var json = JsonConvert.SerializeObject(settings);
      Assert.Contains("\"User\"", json);
      Assert.Contains("\"Audio\"", json);
      Assert.Contains("\"OutputStream\"", json);
      var clone = JsonConvert.DeserializeObject<Settings>(json)!;
      Assert.Equal("N0CALL", clone.User.Call);
      Assert.True(clone.OutputStream.Enabled);
    }
    finally { SettingsLanguage.Set(previous); }
  }

  [Fact]
  public void PropertyGridShowsUserAudioAndOutputStreamAfterLanguageChanges()
  {
    Exception? error = null;
    var thread = new Thread(() =>
    {
      var previous = SettingsLanguage.Current;
      try
      {
        using var form = new Form { Width = 680, Height = 840 };
        using var grid = new PropertyGrid {
          Dock = DockStyle.Fill, PropertySort = PropertySort.NoSort,
          ToolbarVisible = false
        };
        form.Controls.Add(grid);
        var edited = new Settings();
        SettingsLanguage.Set(UiLanguage.English);
        grid.SelectedObject = edited;
        form.Show();
        Application.DoEvents();

        static string[] VisibleRootLabels(PropertyGrid propertyGrid)
        {
          GridItem root = propertyGrid.SelectedGridItem ??
            throw new InvalidOperationException("PropertyGrid has no selected item");
          while (root.Parent != null) root = root.Parent;
          return root.GridItems.Cast<GridItem>()
            .Where(item => item.GridItemType == GridItemType.Property)
            .Select(item => item.Label).ToArray();
        }

        var english = VisibleRootLabels(grid);
        Assert.Contains("Orbit / Ephemeris Sources", english);
        Assert.Contains("User", english);
        Assert.Contains("Audio", english);
        Assert.Contains("OutputStream", english);
        Assert.True(Array.IndexOf(english, "User") <
          Array.IndexOf(english, "Telemetry"));
        Assert.True(Array.IndexOf(english, "OutputStream") <
          Array.IndexOf(english, "Telemetry"));

        SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
        grid.SelectedObject = null;
        grid.SelectedObject = edited;
        Application.DoEvents();
        var chinese = VisibleRootLabels(grid);
        Assert.Contains("轨道与星历来源", chinese);
        Assert.Contains("用户信息", chinese);
        Assert.Contains("音频", chinese);
        Assert.Contains("音频输出流", chinese);
        Assert.True(Array.IndexOf(chinese, "用户信息") <
          Array.IndexOf(chinese, "卫星遥测"));
        Assert.True(Array.IndexOf(chinese, "音频输出流") <
          Array.IndexOf(chinese, "卫星遥测"));
        Assert.Same(edited, grid.SelectedObject);
      }
      catch (Exception ex) { error = ex; }
      finally { SettingsLanguage.Set(previous); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
    if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
  }

  [Fact]
  public void SettingsDialogToolbarsDoNotOccludeTopPropertyGridSections()
  {
    Exception? error = null;
    var thread = new Thread(() =>
    {
      try
      {
        using var dialog = new SettingsDialog();
        var fields = typeof(SettingsDialog).GetFields(
          System.Reflection.BindingFlags.Instance |
          System.Reflection.BindingFlags.NonPublic);
        var grid = (PropertyGrid)fields.Single(field => field.Name == "grid")
          .GetValue(dialog)!;
        grid.SelectedObject = new Settings();

        var flags = System.Reflection.BindingFlags.Instance |
          System.Reflection.BindingFlags.NonPublic;
        typeof(SettingsDialog).GetMethod("InstallLanguageSelector", flags)!
          .Invoke(dialog, null);
        typeof(SettingsDialog).GetMethod("InstallSignedCableToolbar", flags)!
          .Invoke(dialog, null);
        var installIcomLanToolbar = typeof(SettingsDialog)
          .GetMethod("InstallIcomLanToolbar", flags);
        Assert.NotNull(installIcomLanToolbar);
        installIcomLanToolbar.Invoke(dialog, null);

        dialog.CreateControl();
        dialog.PerformLayout();
        var layout = (TableLayoutPanel)fields.Single(field =>
          field.Name == "settingsLayout").GetValue(dialog)!;
        layout.PerformLayout();
        var cable = layout.GetControlFromPosition(0, 0);
        var language = layout.GetControlFromPosition(0, 1);
        var directLan = layout.GetControlFromPosition(0, 2);

        Assert.NotNull(cable);
        Assert.NotNull(language);
        Assert.NotNull(directLan);
        Assert.Same(grid, layout.GetControlFromPosition(0, 3));
        Assert.Same(layout, grid.Parent);
        // Previous direct Dock=Top controls covered the first four rows.
        Assert.True(grid.Top >= cable!.Bottom);
        Assert.True(grid.Top >= language!.Bottom);
        Assert.True(grid.Top >= directLan!.Bottom);
        Assert.True(grid.Height > 400);
        Assert.True(grid.Bottom <= layout.ClientSize.Height);
        Assert.Equal(4, layout.RowCount);
      }
      catch (Exception ex) { error = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
    if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
  }

  [Fact]
  public void InvalidSavedLanguageFallsBackToChinese()
  {
    var previous = SettingsLanguage.Current;
    try
    {
      SettingsLanguage.Set((UiLanguage)999);
      Assert.Equal(UiLanguage.SimplifiedChinese, SettingsLanguage.Current);
    }
    finally { SettingsLanguage.Set(previous); }
  }
}
