using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class UiLocalizationTests
{
  [Fact]
  public void ResourceLookupAndSettingsUseStableLanguageEnum()
  {
    UiLanguage previous = SettingsLanguage.Current;
    try
    {
      UiLocalizationService.Apply(UiLanguage.English);
      Assert.Equal("&File", UiLocalizationService.Get("Menu.File"));
      Assert.Equal("Telemetry", UiLocalizationService.Get("Panel.Telemetry"));
      var settings = new Settings();
      settings.Ui.Language = UiLanguage.English;
      Assert.Contains("\"Language\":0",
        JsonConvert.SerializeObject(settings).Replace(" ", ""));
      UiLocalizationService.Apply(UiLanguage.SimplifiedChinese);
      Assert.Equal("文件(&F)", UiLocalizationService.Get("Menu.File"));
      Assert.Equal("卫星遥测", UiLocalizationService.Get("Panel.Telemetry"));
    }
    finally { UiLocalizationService.Apply(previous); }
  }

  [Fact]
  public void SwitchingLanguageRefreshesLiveViewsAndPreservesControls()
  {
    Exception? error = null;
    var thread = new Thread(() =>
    {
      var previous = SettingsLanguage.Current;
      try
      {
        using var control = new Control();
        int updates = 0;
        UiLocalizationService.Bind(control, () =>
        {
          updates++;
          control.Text = UiLocalizationService.Get("Common.Save");
        });
        UiLocalizationService.Apply(UiLanguage.English);
        Assert.Equal("Save", control.Text);
        UiLocalizationService.Apply(UiLanguage.SimplifiedChinese);
        Assert.Equal("保存", control.Text);
        Assert.True(updates >= 2);
        Assert.False(control.IsDisposed);
        control.Dispose();
        UiLocalizationService.Apply(UiLanguage.English);
        Assert.True(updates >= 2); // disposed listener no longer runs
      }
      catch (Exception ex) { error = ex; }
      finally { UiLocalizationService.Apply(previous); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(12)));
    if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
  }

  [Fact]
  public void PropertyGridAndUiResourcesShareOneCurrentLanguage()
  {
    UiLanguage previous = SettingsLanguage.Current;
    try
    {
      SettingsLanguage.Set(UiLanguage.SimplifiedChinese);
      Assert.Equal("设置", UiLocalizationService.Get("Common.Settings"));
      var zh = TypeDescriptor.GetProperties(new Settings()).Find("Audio", false);
      Assert.Equal("音频", zh!.DisplayName);
      SettingsLanguage.Set(UiLanguage.English);
      Assert.Equal("Settings", UiLocalizationService.Get("Common.Settings"));
      var en = TypeDescriptor.GetProperties(new Settings()).Find("Audio", false);
      Assert.Equal("Audio", en!.DisplayName);
    }
    finally { UiLocalizationService.Apply(previous); }
  }
}
