using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof
{
  public partial class MainForm
  {
    private readonly ToolStripMenuItem uiLanguageMenu = new()
    {
      Name = "UiLanguageMNU"
    };
    private readonly ToolStripMenuItem uiEnglishMenu = new()
    {
      Name = "UiEnglishMNU", Tag = UiLanguage.English
    };
    private readonly ToolStripMenuItem uiChineseMenu = new()
    {
      Name = "UiChineseMNU", Tag = UiLanguage.SimplifiedChinese
    };

    private static readonly IReadOnlyDictionary<string, string> MenuResources =
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["fileToolStripMenuItem"] = "Menu.File",
        ["ExitMNU"] = "Menu.Exit",
        ["GroupViewPanelMNU"] = "Menu.View",
        ["GroupViewMNU"] = "Menu.Group",
        ["SatelliteDetailsMNU"] = "Menu.SatelliteDetails",
        ["TransmittersMNU"] = "Menu.Transmitters",
        ["SatellitePassesMNU"] = "Menu.SatellitePasses",
        ["WaterfallMNU"] = "Menu.Waterfall",
        ["FrequencyControlMNU"] = "Menu.FrequencyControl",
        ["IcomLanSpectrumMNU"] = "Menu.IcomLanSpectrum",
        ["TimelineMNU"] = "Menu.Timeline",
        ["SkyViewMNU"] = "Menu.SkyView",
        ["EarthViewMNU"] = "Menu.EarthView",
        ["QsoEntryMNU"] = "Menu.QsoEntry",
        ["Ft4ConsoleMNU"] = "Menu.Ft4Console",
        ["CwConsoleMNU"] = "Menu.CwConsole",
        ["SstvConsoleMNU"] = "Menu.SstvConsole",
        ["RecorderMNU"] = "Menu.Recorder",
        ["QsoSchedulerMNU"] = "Menu.QsoScheduler",
        ["TelemetryMNU"] = "Menu.Telemetry",
        ["AutoSelectionMNU"] = "Menu.AutoSelection",
        ["ResetWindowLayoutMNU"] = "Menu.ResetLayout",
        ["toolsToolStripMenuItem"] = "Menu.Tools",
        ["SatelliteGroupsMNU"] = "Menu.SatellitesAndGroups",
        ["SdrDevicesMNU"] = "Menu.SdrDevices",
        ["SettingsMNU"] = "Menu.Settings",
        ["ThemeMNU"] = "Menu.Theme",
        ["ThemeSystemMNU"] = "Menu.ThemeSystem",
        ["ThemeLightMNU"] = "Menu.ThemeLight",
        ["ThemeDarkMNU"] = "Menu.ThemeDark",
        ["UiLanguageMNU"] = "Menu.Language",
        ["DownloadSatDataMNU"] = "Menu.DownloadSatelliteData",
        ["DownloadTleMNU"] = "Menu.DownloadOrbitElements",
        ["DownloadAmsatMNU"] = "Menu.DownloadAmsatStatuses",
        ["LoadTleMNU"] = "Menu.LoadOrbitFile",
        ["helpToolStripMenuItem"] = "Menu.Help",
        ["OnlineHelpMNU"] = "Menu.OnlineHelp",
        ["SupportGroupMNU"] = "Menu.SupportGroup",
        ["DataFolderMNU"] = "Menu.DataFolder",
        ["AboutMNU"] = "Menu.About"
      };

    private static readonly IReadOnlyDictionary<Type, string> PanelTitles =
      new Dictionary<Type, string>
      {
        [typeof(GroupViewPanel)] = "Panel.Groups",
        [typeof(SatelliteDetailsPanel)] = "Panel.SatelliteDetails",
        [typeof(TransmittersPanel)] = "Panel.Transmitters",
        [typeof(PassesPanel)] = "Panel.Passes",
        [typeof(TimelinePanel)] = "Panel.Timeline",
        [typeof(SkyViewPanel)] = "Panel.SkyView",
        [typeof(EarthViewPanel)] = "Panel.EarthView",
        [typeof(FrequencyControlPanel)] = "Panel.FrequencyControl",
        [typeof(IcomLanSpectrumPanel)] = "Panel.IcomLanSpectrum",
        [typeof(OnvifCameraPanel)] = "Panel.OnvifCamera",
        [typeof(WaterfallPanel)] = "Panel.Waterfall",
        [typeof(QsoEntryPanel)] = "Panel.QsoEntry",
        [typeof(Ft4ConsolePanel)] = "Panel.Ft4Console",
        [typeof(CwConsolePanel)] = "Panel.CwConsole",
        [typeof(SstvConsolePanel)] = "Panel.SstvConsole",
        [typeof(RecorderPanel)] = "Panel.Recorder",
        [typeof(QsoSchedulerPanel)] = "Panel.QsoScheduler",
        [typeof(TelemetryPanel)] = "Panel.Telemetry",
        [typeof(AutoSelectionPanel)] = "Panel.AutoSelection"
      };

    private void InitializeUiLocalization()
    {
      uiEnglishMenu.Text = "English";
      uiChineseMenu.Text = "简体中文";
      uiEnglishMenu.Click += (_, _) => SwitchUiLanguage(UiLanguage.English);
      uiChineseMenu.Click += (_, _) => SwitchUiLanguage(UiLanguage.SimplifiedChinese);
      uiLanguageMenu.DropDownItems.AddRange(new ToolStripItem[]
      {
        uiChineseMenu, uiEnglishMenu
      });
      int idx = toolsToolStripMenuItem.DropDownItems.IndexOf(ThemeMNU);
      toolsToolStripMenuItem.DropDownItems.Insert(idx < 0 ? 0 : idx + 1, uiLanguageMenu);
      UiLocalizationService.Bind(this, ApplyUiLocalization);
    }

    private void SwitchUiLanguage(UiLanguage language)
    {
      if (ctx.Settings.Ui.Language == language && UiLocalizationService.Current == language)
        return;
      ctx.Settings.Ui.Language = language;
      UiLocalizationService.Apply(language);
      ctx.Settings.SaveToFile();
    }

    private static IEnumerable<ToolStripItem> EnumerateMenuItems(ToolStripItemCollection items)
    {
      foreach (ToolStripItem item in items)
      {
        yield return item;
        if (item is ToolStripDropDownItem dropdown)
          foreach (ToolStripItem child in EnumerateMenuItems(dropdown.DropDownItems))
            yield return child;
      }
    }

    private void ApplyUiLocalization()
    {
      foreach (var item in EnumerateMenuItems(menuStrip1.Items))
        if (MenuResources.TryGetValue(item.Name, out string? key))
          item.Text = UiLocalizationService.Get(key);

      // These toolbars are generated by code rather than the WinForms designer.
      toolbarScaleMenu.Text = UiLocalizationService.Get("Menu.ToolbarScale");
      dockScaleMenu.Text = UiLocalizationService.Get("Menu.DockScale");
      uiChineseMenu.Checked = UiLocalizationService.Current == UiLanguage.SimplifiedChinese;
      uiEnglishMenu.Checked = UiLocalizationService.Current == UiLanguage.English;
      UpdateCwConsoleMenuText();
      // Embedded toolbar UserControls are not DockContent; they need their own
      // explicit static-control translation after the language changes.
      UiLocalizationService.ApplyStaticControls(SatelliteSelecionWidget);
      UiLocalizationService.ApplyStaticControls(FrequencyWidget);
      UiLocalizationService.ApplyStaticControls(GainWidget);
      UiLocalizationService.ApplyStaticControls(RotatorWidget);
      FrequencyWidget.RefreshLocalizedUi();
    }

    private void RegisterPanelLocalization(DockContent panel)
    {
      if (!PanelTitles.TryGetValue(panel.GetType(), out string? key)) return;
      UiLocalizationService.Bind(panel, () =>
      {
        string title = UiLocalizationService.Get(key);
        panel.Text = title;
        panel.TabText = title;
        UiLocalizationService.ApplyStaticControls(panel);
        if (panel is RecorderPanel recorder) recorder.RefreshLocalizedUi();
        if (panel is CwConsolePanel cw) cw.RefreshLocalizedUi();
        if (panel is SstvConsolePanel sstv) sstv.RefreshLocalizedUi();
        if (panel is IcomLanSpectrumPanel spectrum) spectrum.RefreshLocalizedUi();
        if (panel is TelemetryPanel telemetry) telemetry.RefreshLocalizedUi();
        if (panel is AutoSelectionPanel autoSelection) autoSelection.UpdateStatus();
        if (panel is Ft4ConsolePanel ft4Panel) ft4Panel.RefreshLocalizedUi();
        if (panel is QsoSchedulerPanel scheduler) scheduler.RefreshLocalizedUi();
        if (panel is TransmittersPanel transmitters) transmitters.RefreshLocalizedUi();
        if (panel is FrequencyControlPanel frequency) frequency.RefreshLocalizedUi();
        if (panel is GroupViewPanel group) group.RefreshLocalizedUi();
        if (panel is SkyViewPanel sky) sky.RefreshLocalizedUi();
      });
    }
  }
}
