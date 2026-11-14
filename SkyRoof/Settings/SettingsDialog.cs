using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using SkyRoof.IF;
using System.Text.RegularExpressions;
using Accessibility;
using VE3NEA;
using Serilog;

namespace SkyRoof

{
  internal partial class SettingsDialog : Form
  {
    private readonly Context ctx;
    private readonly List<string> ChangedFields = new();
    private UiLanguage appliedLanguage = UiLanguage.SimplifiedChinese;
    private ComboBox? languageSelector;
    private Label? languageLabel;
    private Button? readIcomLanButton;
    private Label? readIcomLanHint;
    private Button? updateOrekitDataButton;
    private readonly CancellationTokenSource orekitRefreshCancellation = new();
    private Label? orekitDataHint;
    private Button? cableOfficialDownload;
    private Button? cableRefresh;
    private Button? cableUninstall;
    private TableLayoutPanel? settingsLayout;


    internal SettingsDialog()
    {
      InitializeComponent();
    }

    internal SettingsDialog(Context ctx, string section = null)
    {
      InitializeComponent();
      this.ctx = ctx;
      appliedLanguage = ctx.Settings.Ui.Language;
      SettingsLanguage.Set(appliedLanguage);
      grid.SelectedObject = Utils.DeepClone(ctx.Settings);
      InstallLanguageSelector();
      InstallSignedCableToolbar();
      InstallIcomLanToolbar();
      grid.SelectedGridItemChanged += (_, _) => UpdateOrekitToolbarState();
      ctx.CatControl.IcomNetworkReadbackReceived += OnIcomNetworkReadback;

      if (section == null)
        grid.CollapseAllGridItems();
      else
        grid.ExpandAndSelect(grid.GetItemByFullName(section));
      UpdateOrekitToolbarState();
    }

    private void InstallLanguageSelector()
    {
      // Place the selector outside the PropertyGrid. It must remain reachable
      // even when categories are collapsed, and is never serialized as a
      // translated string or accidentally submitted as a radio setting.
      var languageBar = new Panel
      {
        Dock = DockStyle.Top, Height = 40,
        Padding = new Padding(8, 5, 8, 3),
        BackColor = SystemColors.Control
      };
      languageLabel = new Label
      {
        AutoSize = false, Location = new Point(9, 9),
        Width = 185, Height = 24,
        TextAlign = ContentAlignment.MiddleLeft
      };
      languageSelector = new ComboBox
      {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 171, Height = 28,
        Anchor = AnchorStyles.Top | AnchorStyles.Right
      };
      languageSelector.Items.AddRange(new object[]
      {
        "简体中文", "English"
      });
      languageBar.Controls.Add(languageSelector);
      languageBar.Controls.Add(languageLabel);
      languageBar.Resize += (_, _) =>
      {
        if (languageSelector == null) return;
        languageSelector.Left = Math.Max(194, languageBar.ClientSize.Width -
          languageSelector.Width - 10);
      };
      languageSelector.SelectedIndex = appliedLanguage ==
        UiLanguage.English ? 1 : 0;
      languageSelector.SelectedIndexChanged += (_, _) =>
      {
        if (grid.SelectedObject is not Settings edited) return;
        UiLanguage selected = languageSelector.SelectedIndex == 1
          ? UiLanguage.English : UiLanguage.SimplifiedChinese;
        edited.Ui.Language = selected;
        SettingsLanguage.Set(selected);
        // Rebuild labels while preserving the same edited settings instance.
        // This avoids discarding uncommitted changes in other categories.
        grid.SelectedObject = null;
        grid.SelectedObject = edited;
        RefreshLanguageCaptions();
      };
      // A dedicated table avoids overlaying the top of the PropertyGrid.
      // Previously the two new Dock=Top toolbars were placed over the
      // existing Dock=Fill grid, hiding its first four Settings sections.
      // Explicit rows guarantee that the grid begins BELOW both toolbars.
      settingsLayout = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        ColumnCount = 1,
        RowCount = 4
      };
      settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
      settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37F));
      settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
      settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37F));
      settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
      languageBar.Dock = DockStyle.Fill;
      grid.Dock = DockStyle.Fill;
      grid.Margin = Padding.Empty;
      Controls.Remove(grid);
      settingsLayout.Controls.Add(languageBar, 0, 1);
      settingsLayout.Controls.Add(grid, 0, 3);
      Controls.Add(settingsLayout);
      // Match the original Designer ordering of Dock=Fill grid and bottom
      // command button panel. The TableLayoutPanel owns all top spacing.
      Controls.SetChildIndex(settingsLayout, 0);
      RefreshLanguageCaptions();
    }

    private void InstallSignedCableToolbar()
    {
      var bar = new FlowLayoutPanel
      {
        Dock = DockStyle.Top, Height = 37,
        FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
        Padding = new Padding(8, 3, 8, 3)
      };
      cableOfficialDownload = new Button { Width = 166, Height = 28 };
      cableUninstall = new Button { Width = 166, Height = 28 };
      cableRefresh = new Button { Width = 158, Height = 28 };
      cableOfficialDownload.Click += async (_, _) =>
        await ManageVendorCableAsync("Install");
      cableUninstall.Click += async (_, _) =>
        await ManageVendorCableAsync("Uninstall");
      cableRefresh.Click += (_, _) => grid.Refresh();
      bar.Controls.Add(cableOfficialDownload);
      bar.Controls.Add(cableUninstall);
      bar.Controls.Add(cableRefresh);
      bar.Dock = DockStyle.Fill;
      if (settingsLayout == null)
        throw new InvalidOperationException("Settings layout has not been initialized.");
      settingsLayout.Controls.Add(bar, 0, 0);
      RefreshLanguageCaptions();
    }

    private void InstallIcomLanToolbar()
    {
      var bar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 3, 8, 3) };
      readIcomLanButton = new Button
      {
        Left = 8, Top = 3, Width = 171, Height = 29,
        Text = "READ FROM CI-V"
      };
      readIcomLanHint = new Label
      {
        Left = 188, Top = 4, Height = 27,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
      };
      updateOrekitDataButton = new Button
      {
        Left = 8, Top = 3, Width = 171, Height = 29
      };
      orekitDataHint = new Label
      {
        Left = 188, Top = 4, Height = 27,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
      };
      updateOrekitDataButton.Click += async (_, _) => await UpdateOrekitDataAsync();
      bar.Controls.Add(readIcomLanButton);
      bar.Controls.Add(readIcomLanHint);
      bar.Controls.Add(updateOrekitDataButton);
      bar.Controls.Add(orekitDataHint);
      bar.Resize += (_, _) =>
      {
        int hintWidth = Math.Max(80, bar.ClientSize.Width - 197);
        if (readIcomLanHint != null) readIcomLanHint.Width = hintWidth;
        if (orekitDataHint != null) orekitDataHint.Width = hintWidth;
      };
      readIcomLanButton.Click += (_, _) =>
      {
        if (ctx.CatControl.RequestIcomNetworkReadback())
        {
          if (readIcomLanHint is Label hint)
            hint.Text = SettingsLanguage.Text(
            "正在读取 IC-9700 网络参数...", "Reading IC-9700 LAN parameters...");
        }
        else
        {
          MessageBox.Show(SettingsLanguage.Text(
            "需要已连接 IC-9700 的 SkyCAT CAT 通道；当前 CAT 后端无法执行只读 CI-V 网络查询。",
            "An active IC-9700 SkyCAT CAT connection is required to read network settings."),
            "IC-9700", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
      };
      settingsLayout?.Controls.Add(bar, 0, 2);
      RefreshLanguageCaptions();
    }

    private void UpdateOrekitToolbarState()
    {
      bool isOrekit = false;
      for (GridItem? item = grid.SelectedGridItem; item != null; item = item.Parent)
      {
        if (item.PropertyDescriptor?.Name == nameof(Settings.OrbitSources))
        {
          isOrekit = true;
          break;
        }
      }

      if (readIcomLanButton != null) readIcomLanButton.Visible = !isOrekit;
      if (readIcomLanHint != null) readIcomLanHint.Visible = !isOrekit;
      if (updateOrekitDataButton != null) updateOrekitDataButton.Visible = isOrekit;
      if (orekitDataHint != null) orekitDataHint.Visible = isOrekit;
    }

    private async Task UpdateOrekitDataAsync()
    {
      if (grid.SelectedObject is not Settings edited || updateOrekitDataButton == null)
        return;
      updateOrekitDataButton.Enabled = false;
      if (orekitDataHint != null)
        orekitDataHint.Text = SettingsLanguage.Text("正在更新 IERS / DE430...", "Updating IERS / DE430...");
      try
      {
        // Keep the network fetch, checksum, and file I/O off the WinForms
        // synchronization context even when the cache is already present.
        var sources = new OrbitSourceSettings
        {
          OrekitDataDirectory = edited.OrbitSources.OrekitDataDirectory,
          OrekitEopSources = edited.OrbitSources.OrekitEopSources,
          OrekitDe430Url = edited.OrbitSources.OrekitDe430Url
        };
        await Task.Run(() => OrekitDataBootstrapper.EnsureAsync(
          sources, forceRefresh: true, cancellationToken: orekitRefreshCancellation.Token),
          orekitRefreshCancellation.Token);
        if (IsDisposed || Disposing || orekitRefreshCancellation.IsCancellationRequested) return;
        string directory = OrbitBackendRegistry.ResolveDataDirectory(
          sources.OrekitDataDirectory);
        bool hasEop = File.Exists(Path.Combine(directory, "finals2000A.all"));
        bool hasDe430 = File.Exists(Path.Combine(directory, "lnxp1990.430"));
        grid.Refresh();
        string message = SettingsLanguage.Text(
          $"Orekit 数据目录：{directory}\r\nIERS EOP：{(hasEop ? "已缓存" : "缺失")}\r\nDE430：{(hasDe430 ? "已缓存" : "缺失")}\r\n\r\n重启 SkyRoof 后加载更新的 EOP / 星历。如已修改数据目录，请点击应用保存配置。",
          $"Orekit data directory: {directory}\r\nIERS EOP: {(hasEop ? "cached" : "missing")}\r\nDE430: {(hasDe430 ? "cached" : "missing")}\r\n\r\nRestart SkyRoof to load updated EOP / ephemeris. Apply the settings to save a changed directory.");
        if (orekitDataHint != null)
          orekitDataHint.Text = SettingsLanguage.Text(
            hasEop && hasDe430 ? "数据已缓存；重启后生效" : "数据不完整；查看提示",
            hasEop && hasDe430 ? "Data cached; restart to load" : "Incomplete data; see details");
        MessageBox.Show(message, "Orekit / Ephemeris", MessageBoxButtons.OK,
          hasEop && hasDe430 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
      }
      catch (OperationCanceledException) when (orekitRefreshCancellation.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        if (IsDisposed || Disposing) return;
        Log.Warning(ex, "Manual Orekit data refresh failed.");
        if (orekitDataHint != null)
          orekitDataHint.Text = SettingsLanguage.Text("更新失败", "Update failed");
        MessageBox.Show(ex.GetBaseException().Message, "Orekit / Ephemeris",
          MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
      finally
      {
        if (!IsDisposed && !Disposing)
          updateOrekitDataButton.Enabled = true;
      }
    }

    private void OnIcomNetworkReadback(IcomNetworkReadbackState snapshot)
    {
      if (IsDisposed || Disposing || !IsHandleCreated) return;
      if (InvokeRequired)
      {
        BeginInvoke(new Action(() => OnIcomNetworkReadback(snapshot)));
        return;
      }
      if (grid.SelectedObject is not Settings edited) return;

      var target = edited.IcomLanSpectrum;
      void Update<T>(string name, T oldValue, T newValue, Action write)
      {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
        write();
        ChangedFields.Add("SkyRoof.IcomLanSpectrumSettings." + name);
      }
      if (snapshot.IpAddress != null)
        Update(nameof(target.RadioAddress), target.RadioAddress,
          snapshot.IpAddress, () => target.RadioAddress = snapshot.IpAddress);
      if (snapshot.ControlPort.HasValue)
        Update(nameof(target.DirectLanControlPort), target.DirectLanControlPort,
          snapshot.ControlPort.Value,
          () => target.DirectLanControlPort = snapshot.ControlPort.Value);
      if (snapshot.SerialPort.HasValue)
        Update(nameof(target.SerialPort), target.SerialPort,
          snapshot.SerialPort.Value,
          () => target.SerialPort = snapshot.SerialPort.Value);

      TypeDescriptor.Refresh(target);
      grid.Refresh();
      string audio = snapshot.AudioPort?.ToString() ?? "—";
      string enabled = snapshot.NetworkEnabled switch
      {
        true => "ON", false => "OFF", _ => "?"
      };
      if (readIcomLanHint is Label resultHint)
        resultHint.Text = snapshot.AvailableCount == 0
        ? SettingsLanguage.Text("未获得有效 CI-V 应答", "No valid CI-V network replies")
        : SettingsLanguage.Text(
          $"已读取 {snapshot.AvailableCount}/5；LAN {enabled}；Audio UDP {audio}",
          $"Read {snapshot.AvailableCount}/5; LAN {enabled}; Audio UDP {audio}");
      // Audio UDP is informational. User credentials are never queried or altered.
      // The modified settings are saved only when Apply/OK is pressed.
    }

    private async Task ManageVendorCableAsync(string action)
    {
      bool uninstall = action == "Uninstall";
      string folder = Path.Combine(AppContext.BaseDirectory, "ThirdParty", "VB-CABLE");
      string manager = Path.Combine(folder, "Manage-VbCable.ps1");
      string archive = Path.Combine(folder, "VBCABLE_Driver_Pack45.zip");
      string managerLog = Path.Combine(folder, "VB-CABLE-manager.log");
      if (!File.Exists(manager) || !File.Exists(archive))
      {
        DialogResult redirect = MessageBox.Show(SettingsLanguage.Text(
            "此构建未捆绑官方 VB-CABLE 安装包。是否打开 VB-Audio 官方下载网站？",
            "This build has no original VB-CABLE package. Open the vendor's official website?"),
          "VB-CABLE", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (redirect == DialogResult.Yes)
          Process.Start(new ProcessStartInfo(IfAfSignedCableDiscovery.VbCableUrl)
          { UseShellExecute = true });
        return;
      }
      if (uninstall && ctx.Settings.IfAf.Enabled)
      {
        MessageBox.Show(SettingsLanguage.Text(
          "先禁用 IF→AF 并应用设置，再卸载共享虚拟声卡。",
          "Disable and apply IF->AF before removing the shared audio cable."),
          "VB-CABLE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }
      string warning = uninstall ?
        SettingsLanguage.Text(
          "VB-CABLE 可能也被其他软件使用。仅允许卸载 SkyRoof 自己安装的实例。确认卸载并在完成后重新启动 Windows 吗？",
          "Other programs may use VB-CABLE. Only SkyRoof-installed instances can be removed. Proceed and reboot afterward?") :
        SettingsLanguage.Text(
          "VB-CABLE 是 VB-Audio 软件（www.vb-cable.com），采用捐赠软件模式，欢迎自愿捐赠。即将经 UAC 提权调用未修改的原厂安装程序，安装后可能需要重启。是否继续？",
          "VB-CABLE by VB-Audio (www.vb-cable.com) is donationware; contributions are welcome. Run the original vendor installer with UAC and reboot if needed?");
      if (MessageBox.Show(warning, "VB-CABLE", MessageBoxButtons.YesNo,
        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        return;
      if (cableOfficialDownload != null) cableOfficialDownload.Enabled = false;
      if (cableUninstall != null) cableUninstall.Enabled = false;
      try
      {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
          "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(exe)
        { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(manager);
        start.ArgumentList.Add("-Action");
        start.ArgumentList.Add(action);
        start.ArgumentList.Add("-Consent");
        using var process = Process.Start(start) ??
          throw new InvalidOperationException("Cannot start the original vendor package manager.");
        await process.WaitForExitAsync();
        grid.Refresh();
        if (process.ExitCode != 0 && process.ExitCode != 3010)
        {
          // The UAC-elevated PowerShell window may close before its error is
          // readable. Show the manager's last diagnostic phase directly.
          string evidence = SettingsLanguage.Text(
            "未找到驱动安装日志。", "No driver installation log was created.");
          try
          {
            if (File.Exists(managerLog))
            {
              string log = File.ReadAllText(managerLog);
              int marker = log.LastIndexOf("VB-CABLE FAILED at stage [",
                StringComparison.Ordinal);
              if (marker >= 0) log = log.Substring(marker);
              evidence = log.Length > 1800 ? log.Substring(log.Length - 1800) : log;
            }
          }
          catch (IOException ex)
          {
            Log.Warning("VB-CABLE installer diagnostic log unreadable ({Type})", ex.GetType().Name);
          }
          catch (UnauthorizedAccessException ex)
          {
            Log.Warning("VB-CABLE installer diagnostic log access denied ({Type})", ex.GetType().Name);
          }
          throw new InvalidOperationException(
            SettingsLanguage.Text(
              $"VB-CABLE 安装失败（退出码 {process.ExitCode}）。\n诊断日志：{managerLog}\n\n{evidence}",
              $"VB-CABLE installation failed (exit code {process.ExitCode}).\nDiagnostic log: {managerLog}\n\n{evidence}"));
        }
        MessageBox.Show(SettingsLanguage.Text(
          "原厂安装/卸载操作已返回。建议重新启动 Windows，再在设置中重新检测 CABLE Input / CABLE Output。",
          "The vendor installer finished. Restart Windows and rescan both CABLE Input and CABLE Output."),
          "VB-CABLE", MessageBoxButtons.OK, MessageBoxIcon.Information);
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message, "VB-CABLE", MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
      }
      finally
      {
        if (cableOfficialDownload != null) cableOfficialDownload.Enabled = true;
        if (cableUninstall != null) cableUninstall.Enabled = true;
      }
    }

    private void RefreshLanguageCaptions()
    {
      Text = SettingsLanguage.Text("设置", "Settings");
      if (cableOfficialDownload != null)
        cableOfficialDownload.Text = SettingsLanguage.Text("安装 VB-CABLE", "Install VB-CABLE");
      if (cableUninstall != null)
        cableUninstall.Text = SettingsLanguage.Text("卸载 VB-CABLE", "Remove VB-CABLE");
      if (cableRefresh != null)
        cableRefresh.Text = SettingsLanguage.Text("重新检测音频端点", "Rescan audio endpoints");
      if (updateOrekitDataButton != null)
        updateOrekitDataButton.Text = SettingsLanguage.Text("更新 Orekit 星历", "Update Orekit Data");
      if (orekitDataHint != null && string.IsNullOrEmpty(orekitDataHint.Text))
        orekitDataHint.Text = SettingsLanguage.Text("IERS EOP / JPL DE430 数据", "IERS EOP / JPL DE430 data");
      if (readIcomLanHint != null && string.IsNullOrEmpty(readIcomLanHint.Text))
        readIcomLanHint.Text = SettingsLanguage.Text(
          "IC-9700 Direct LAN 参数", "IC-9700 Direct LAN settings");
      if (languageLabel != null)
        languageLabel.Text = SettingsLanguage.Text("界面语言", "Interface language");
      applyBtn.Text = SettingsLanguage.Text("应用", "Apply");
      cancelBtn.Text = SettingsLanguage.Text("取消", "Cancel");
      okBtn.Text = SettingsLanguage.Text("确定", "OK");
      resetToolStripMenuItem.Text = SettingsLanguage.Text("重置", "Reset");
    }

    private void SettingsDialog_Shown(object? sender, EventArgs e)
    {
      ctx.Settings.Ui.RestorePropertyGridLabelWidth("SettingsDialog", grid);
    }

    private void SettingsDialog_FormClosing(object? sender, FormClosingEventArgs e)
    {
      orekitRefreshCancellation.Cancel();
      ctx.CatControl.IcomNetworkReadbackReceived -= OnIcomNetworkReadback;
      ctx.Settings.Ui.SavePropertyGridLabelWidth("SettingsDialog", grid);
      // Cancel must also undo a language preview. Apply commits the preview
      // independently of subsequent edits, matching standard Settings UX.
      if (DialogResult != DialogResult.OK)
        SettingsLanguage.Set(appliedLanguage);
    }

    private void SelectSection(string section)
    {
      if (section == null) return;

      var gridItem = grid.GetItemByFullName(section);
      grid.ExpandAndSelect(gridItem);
    }

    private void resetToolStripMenuItem_Click(object sender, EventArgs e)
    {
      string label = PropertyGridEx.GetItemProperty(grid.SelectedGridItem, "HelpKeyword");

      // collection properties don't have a [DefaultValue]; handle their reset explicitly
      var settings = (Settings?)grid.SelectedObject;
      switch (label)
      {
        case "SkyRoof.TransverterSettings.SdrBands":
          settings!.Transverter.ResetSdrBands();
          grid.Refresh();
          break;

        case "SkyRoof.TransverterSettings.CatBands":
          settings!.Transverter.ResetCatBands();
          grid.Refresh();
          break;

        default:
          grid.ResetSelectedProperty();
          break;
      }

      ChangedFields.Add(label);
      RefreshConditionalSettingsWhenModeChanges(label);
    }

    private void RefreshConditionalSettingsWhenModeChanges(string? name)
    {
      bool rotator = name is
        "SkyRoof.RotatorSettings.DriverMode" or
        "SkyRoof.RotatorSettings.NativeTransport" or
        "SkyRoof.RotatorSettings.NativeProtocol";

      bool spectrum = name is
        "SkyRoof.IcomLanSpectrumSettings.Source" or
        "SkyRoof.IcomLanSpectrumSettings.ControlPath";

      if (!rotator && !spectrum) return;

      // PropertyGrid caches nested descriptors while a dropdown is open.
      // Rebuild the affected subsection after its edit is committed, without
      // losing values that are now hidden by the other mode.
      BeginInvoke(new Action(() =>
      {
        if (IsDisposed || grid.IsDisposed) return;
        if (grid.SelectedObject is Settings settings)
        {
          if (rotator) TypeDescriptor.Refresh(settings.Rotator);
          if (spectrum) TypeDescriptor.Refresh(settings.IcomLanSpectrum);
        }
        grid.Refresh();
      }));
    }

    private void applyBtn_Click(object sender, EventArgs e)
    {
      ctx.Settings = Utils.DeepClone((Settings)grid.SelectedObject);
      ApplyChangedSettings();
    }

    private void okBtn_Click(object sender, EventArgs e)
    {
      ctx.Settings = (Settings)grid.SelectedObject;
      ApplyChangedSettings();
    }




    //--------------------------------------------------------------------------------------------------------------
    //                                        validate changes
    //--------------------------------------------------------------------------------------------------------------
    private void grid_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
    {
      bool canChange = true;
      string label = PropertyGridEx.GetItemProperty(e.ChangedItem, "HelpKeyword");

      switch (label)
      {
        case "SkyRoof.UserSettings.Call":
          canChange = ValidateByRegex(e, Utils.CallsignRegex, "callsign", CharacterCasing.Upper);
          break;

        case "SkyRoof.UserSettings.Square":
          canChange = ValidateByRegex(e, Utils.GridSquareExtendedRegex, "grid square", CharacterCasing.Upper);
          break;

        case "SkyRoof.UserSettings.Altitude":
          ValidateInt(e, 8849);
          break;

        case "SkyRoof.AnnouncerSettings.Voice":
          ctx.Announcer.SayVoiceName(e.ChangedItem.Value!.ToString());
          break;

        case "SkyRoof.AnnouncerSettings.Volume":
          ValidateInt(e, 100);
          break;

        case "SkyRoof.AosAnnouncement.Minutes":
          ValidateInt(e, 5);
          break;

        case "SkyRoof.PositionAnnouncement.Degrees":
          ValidateInt(e, 30, 1);
          break;

        case "SkyRoof.OutputStreamSettings.Gain":
          ValidateInt(e, 60, -60);
          break;

        case "SkyRoof.Ft4WaterfallSettings.Bandwidth":
          ValidateInt(e, 5000, 2000);
          break;

        case "SkyRoof.IcomDirectLanReceiveSettings.PrebufferMs":
        case "SkyRoof.IcomDirectLanTransmitSettings.PrebufferMs":
          ValidateInt(e, 500, 20);
          break;

        case "SkyRoof.IcomDirectLanMeterSettings.PollIntervalMs":
          ValidateInt(e, 2000, 100);
          break;

        case "SkyRoof.IcomDirectLanReceiveSettings.SpeakerVolume":
          ValidateInt(e, 100);
          break;

        case "SkyRoof.IcomDirectLanTransmitSettings.MicrophoneGain":
          ValidateInt(e, 200);
          break;

        case "SkyRoof.IcomLanSpectrumSettings.WaterfallRows":
          ValidateInt(e, 800, 40);
          break;

        case "SkyRoof.IcomLanSpectrumSettings.SpectrumHeightPercent":
          ValidateInt(e, 80, 20);
          break;

        case "SkyRoof.IcomLanSpectrumSettings.WaterfallBrightness":
          ValidateInt(e, 80, -80);
          break;

        case "SkyRoof.IcomLanSpectrumSettings.WaterfallContrast":
          ValidateInt(e, 250, 25);
          break;

        case "SkyRoof.IcomLanSpectrumSettings.ScopeEdgeNumber":
          ValidateInt(e, 4, 1);
          break;

        case "SkyRoof.IcomLanSpectrumSettings.ScopeReferenceLevelDb":
          ValidateHalfDb(e);
          break;

        case "SkyRoof.Ft4ConsoleSettings.TxGain":
          ValidateInt(e, 0, -60);
          break;

        case "SkyRoof.Ft4ConsoleSettings.EnableTransmit":
          canChange = ValidateFt4Transmit(e);
          break;

        case "SkyRoof.Ft4ConsoleSettings.TxWatchDog":
          ValidateInt(e, 20, 1);
          break;

        case "SkyRoof.CwConsoleSettings.CwKeyerPort":
          ValidateInt(e, 65535, 1);
          break;

        case "SkyRoof.RotatorSettings.StepSize":
          ValidateFloat(e, 30, 0.01f);
          break;
      }

      if (canChange)
      {
        ChangedFields.Add(label);
        RefreshConditionalSettingsWhenModeChanges(label);
      }
    }

    private bool ValidateFt4Transmit(PropertyValueChangedEventArgs e)
    {
      var value = (bool)e.ChangedItem!.Value!;
      var call = ((Settings)grid.SelectedObject!).User.Call;

      bool error = value && string.IsNullOrEmpty(call);

      if (error)
      {
        MessageBox.Show("Enter your callsign before enabling FT4 transmission.", "SkyRoof", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        e.ChangedItem.PropertyDescriptor.SetValue(e.ChangedItem.Parent.Value, e.OldValue);
      }

      return !error;
    }

    private bool ValidateByRegex(PropertyValueChangedEventArgs e, Regex regEx, string fieldName, CharacterCasing casing)
    {
      string newValue = e.ChangedItem.Value.ToString();
      string cleanValue = Utils.SetCasing(newValue.Trim(), casing);

      if (!regEx.IsMatch(cleanValue))
      {
        e.ChangedItem.PropertyDescriptor.SetValue(e.ChangedItem.Parent.Value, e.OldValue);
        MessageBox.Show($"Invalid {fieldName}: \"{newValue}\"", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        return false;
      }

      if (cleanValue != newValue) e.ChangedItem.PropertyDescriptor.SetValue(e.ChangedItem.Parent.Value, cleanValue);
      return true;
    }

    private void ValidateInt(PropertyValueChangedEventArgs e, int max, int min = 0)
    {
      int cleanValue = Math.Max(min, Math.Min(max, (int)e.ChangedItem.Value));
      e.ChangedItem.PropertyDescriptor.SetValue(e.ChangedItem.Parent.Value, cleanValue);
    }
    private void ValidateFloat(PropertyValueChangedEventArgs e, float max, float min = 0)
    {
      float cleanValue = Math.Max(min, Math.Min(max, (float)e.ChangedItem.Value));
      e.ChangedItem.PropertyDescriptor.SetValue(e.ChangedItem.Parent.Value, cleanValue);
    }

    private void ValidateHalfDb(PropertyValueChangedEventArgs e)
    {
      double value =
        Convert.ToDouble(
          e.ChangedItem.Value);

      double cleanValue =
        Math.Clamp(
          Math.Round(
            value * 2,
            MidpointRounding.AwayFromZero) / 2.0,
          -20.0,
          20.0);

      e.ChangedItem.PropertyDescriptor.SetValue(
        e.ChangedItem.Parent.Value,
        cleanValue);
    }




    //--------------------------------------------------------------------------------------------------------------
    //                                            apply changes
    //--------------------------------------------------------------------------------------------------------------
    private void RebuildOrbitTrackingCaches()
    {
      foreach (var sat in ctx.SatnogsDb.Satellites)
        if (!sat.IsEphemerisTarget)
          sat.SetTle(sat.Tle);
      ctx.GroupPasses.FullRebuild();
      ctx.HamPasses.FullRebuild();
      ctx.SdrPasses.FullRebuild();
    }

    private void ApplyChangedSettings()
    {
      if (ChangedFields.Contains("SkyRoof.UserSettings.Square") ||
          ChangedFields.Contains("SkyRoof.UserSettings.Altitude"))
        ctx.MainForm.SetLocation();

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.OutputStreamSettings.")))
        ctx.MainForm.ApplyOutputStreamSettings();
      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.IF.IfAfSettings.")))
        ctx.MainForm.ApplyIfAfSettings();

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.KissServerSettings.")))
        ctx.MainForm.ApplyKissServerSettings();

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.AudioSettings.")))
        ctx.MainForm.ApplyAudioSettings();

      if (ChangedFields.Exists(s =>
            s is "SkyRoof.TelemetrySettings.InputSource"
              or "SkyRoof.TelemetrySettings.IfCaptureDeviceId"
              or "SkyRoof.TelemetrySettings.AfCaptureDeviceId"
              or "SkyRoof.TelemetrySettings.AfLoopbackDeviceId"
              or "SkyRoof.TelemetrySettings.AfCenterHz"))
        ctx.TelemetryPanel?.ApplyAudioSettings();

      if (ChangedFields.Exists(s =>
            s.StartsWith("SkyRoof.CwConsoleSettings.")))
      {
        ctx.CwAudio?.ApplySettings();
        ctx.MainForm.UpdateCwConsoleMenuText();

        if (ctx.CwTransmit != null)
        {
          try
          {
            ctx.CwTransmit
              .ApplySettingsAsync(
                ctx.Settings.CwConsole)
              .GetAwaiter()
              .GetResult();
          }
          catch (Exception ex)
          {
            MessageBox.Show(
              "CW transmit settings were applied, but the active transmitter could not be stopped cleanly:\r\n\r\n" +
              ex.Message,
              "CW TX",
              MessageBoxButtons.OK,
              MessageBoxIcon.Warning);
          }
        }
      }

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.Announcement.Minutes")) ||
          ChangedFields.Exists(s => s.StartsWith("SkyRoof.Announcement.Enabled")))
        ctx.Announcer.RebuildQueue();

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.CatSettings.")) ||
        ChangedFields.Exists(s => s.StartsWith("SkyRoof.CatRadioSettings")) ||
        ChangedFields.Exists(s => s.StartsWith("SkyRoof.EmbeddedSkyCatSettings.")))
      {
        ctx.CatControl.ApplySettings();
        ctx.MainForm.ShowCatStatus();
      }

      // HardwareModel selects an operator UI only. Do not disconnect a
      // tracking antenna or restart its serial owner for a cosmetic switch.
      if (ChangedFields.Exists(s =>
            s.StartsWith("SkyRoof.RotatorSettings.") &&
            s != "SkyRoof.RotatorSettings.HardwareModel"))
        ctx.RotatorControl.ApplySettings(true);
      else if (ChangedFields.Contains("SkyRoof.RotatorSettings.HardwareModel"))
        ctx.MainForm.ShowRotatorStatus();

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.WaterfallSettings.")))
        ctx.WaterfallPanel?.ApplySettings();

      IcomLanSettingsAction lanAudioChange =
        IcomLanSettingsChangePolicy.Classify(ChangedFields);
      if (lanAudioChange != IcomLanSettingsAction.None)
      {
        if (lanAudioChange == IcomLanSettingsAction.RenegotiateMedia)
        {
          if (ctx.IcomLanSpectrumPanel != null)
            ctx.IcomLanSpectrumPanel.ApplySettings();
          else
            ctx.DirectLanMediaOwner?.ReconfigureIfActive();
        }
        else
          ctx.DirectLanMediaOwner?.ApplyCurrentSettings(
            lanAudioChange == IcomLanSettingsAction.RestartWindowsEndpoints);
        ctx.MainForm.UpdateIcomDirectLanMeterBar();
      }

      if (ChangedFields.Exists(
            s => s.StartsWith(
              "SkyRoof.IcomLanSpectrumSettings.")))
      {
        bool transportChanged =
          ChangedFields.Exists(
            s =>
              s is
                "SkyRoof.IcomLanSpectrumSettings.RadioAddress" or
                "SkyRoof.IcomLanSpectrumSettings.SerialPort" or
                "SkyRoof.IcomLanSpectrumSettings.Source" or
                "SkyRoof.IcomLanSpectrumSettings.SkyCatScopePort" or
                "SkyRoof.IcomLanSpectrumSettings.DirectLanControlPort" or
                "SkyRoof.IcomLanSpectrumSettings.DirectLanUsername" or
                "SkyRoof.IcomLanSpectrumSettings.DirectLanPassword" or
                "SkyRoof.IcomLanSpectrumSettings.DirectLanClientName");

        bool controlChanged =
          ChangedFields.Exists(
            s =>
              s is
                "SkyRoof.IcomLanSpectrumSettings.ControlPath" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeBand" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeEdgeNumber" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeReferenceLevelDb" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeSweepSpeed" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeDuringTx" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeCenterType" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeVbw" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeMarkerPosition");

        bool advancedControlChanged =
          ChangedFields.Exists(
            s =>
              s is
                "SkyRoof.IcomLanSpectrumSettings.ScopeDuringTx" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeCenterType" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeVbw" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeRbw" or
                "SkyRoof.IcomLanSpectrumSettings.ScopeMarkerPosition");

        if (advancedControlChanged)
          ctx.Settings.IcomLanSpectrum
            .ManageAdvancedScopeControls =
            true;

        if (transportChanged)
        {
          if (ctx.IcomLanSpectrumPanel != null)
            ctx.IcomLanSpectrumPanel.ApplySettings();
          else
            ctx.DirectLanMediaOwner?.ReconfigureIfActive();
        }
        else if (controlChanged)
          ctx.IcomLanSpectrumPanel?.ApplyControlSettings();
        else
          ctx.IcomLanSpectrumPanel?.ApplyDisplaySettings();
      }

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.OrbitSourceSettings.")))
        ctx.SatnogsDb.ConfigureSources(ctx.Settings.OrbitSources);

      // Orekit caches its global UTC/ITRF data context. Never attempt to
      // re-initialize a different data directory within the current process.
      // These persisted changes are picked up by the startup bootstrap.
      if (ChangedFields.Exists(s =>
            s is "SkyRoof.OrbitSourceSettings.OrekitDataDirectory" or
                 "SkyRoof.OrbitSourceSettings.ShowSolarSystemTargets"))
        MessageBox.Show(SettingsLanguage.Text(
          "Orekit 数据目录或太阳系目标设置已保存。请重启 SkyRoof，使轨道与星历计算统一使用新设置。",
          "The Orekit data directory or solar-system target settings were saved. Restart SkyRoof to apply the changes to the orbit and ephemeris engine."),
          "Orekit / Ephemeris", MessageBoxButtons.OK, MessageBoxIcon.Information);

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.AmsatSettings.")))
        if (ctx.Settings.Amsat.Enabled)
          ctx.AmsatStatusLoader.GetStatusesAsync().DoNotAwait();
        else
          ctx.GroupViewPanel?.ShowAmsatStatuses();

      if (ChangedFields.Exists(s => s.StartsWith("SkyRoof.QsoEntrySettings.")))
        ctx.QsoEntryPanel?.ApplySettings();

      if (ChangedFields.Exists(s => 
        s.StartsWith("SkyRoof.Ft4ConsoleSettings.") ||
        s.StartsWith("SkyRoof.Ft4ReceiveSettings.") ||
        s.StartsWith("SkyRoof.Ft4TransmitSettings.") ||
        s.StartsWith("SkyRoof.Ft4WaterfallSettings.") ||
        s.StartsWith("SkyRoof.Ft4MessagesSettings.") ||
        s.StartsWith("SkyRoof.UdpSenderSettings.") ||
        s.StartsWith("SkyRoof.Ft4BackgroundColors.") ||
        s.StartsWith("SkyRoof.UserSettings.Call")
        ))
        ctx.Ft4ConsolePanel?.ApplySettings();

      if (appliedLanguage != ctx.Settings.Ui.Language)
      {
        SettingsLanguage.Set(ctx.Settings.Ui.Language);
        appliedLanguage = ctx.Settings.Ui.Language;
        ctx.Settings.SaveToFile();
      }
      ChangedFields.Clear();
    }
  }
}
