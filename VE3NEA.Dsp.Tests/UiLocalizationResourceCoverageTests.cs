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
}
