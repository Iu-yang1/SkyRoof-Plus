using System.Diagnostics;
using System.Runtime;
using NAudio.CoreAudioApi;
using MathNet.Numerics;
using Serilog;
using SGPdotNET.Observation;
using SkyRoof.CW;
using SkyRoof.IF;
using VE3NEA;
using VE3NEA.Clock;
using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof
{
  public partial class MainForm : Form
  {
    internal Context ctx = new();
    private RotatorControlCard? RotatorControlCard;
    private ToolStripControlHost? RotatorControlHost;
    private ToolStripDropDown? RotatorControlPopup;
    private YaesuG5500ControlCard? YaesuG5500ControlCard;
    private ToolStripControlHost? YaesuG5500ControlHost;
    private ToolStripDropDown? YaesuG5500ControlPopup;
    private MouseButtons LastRotatorStatusMouseButton;
    private ToolStripProfessionalRenderer? ThemeRenderer;
    private UniformControlScale? toolbarScale;
    private bool toolbarScaling;
    private readonly ToolStripMenuItem toolbarScaleMenu = new("UI Scale");
    private float lastToolbarScale = 1f;
    private readonly ToolStripMenuItem dockScaleMenu = new("Dock & Popup Scale");
    private readonly Dictionary<DockContent, UniformControlScale> dockScalers = new();
    private readonly HashSet<DockContent> registeredDockPanels = new();
    private UniformControlScale? genericRotatorPopupScale;
    private UniformControlScale? yaesuRotatorPopupScale;

    private readonly EmbeddedSkyCatHost EmbeddedSkyCat = new();
    private readonly ToolStripStatusLabel[] ifAfStageLeds = new ToolStripStatusLabel[4];
    private readonly ToolStripStatusLabel[] ifAfStageLabels = new ToolStripStatusLabel[4];
    private readonly IcomDirectLanMeterBar directLanMeters = new();
    private ToolStripControlHost? directLanMetersHost;
    private readonly ToolStripStatusLabel directLanTxAudioLabel = new()
    {
      Name = "IcomDirectLanTxAudio",
      Alignment = ToolStripItemAlignment.Right,
      AutoSize = true,
      Visible = false,
      Text = "LAN TX offline",
      Margin = new Padding(4, 3, 4, 2),
      ToolTipText = "LAN transmit audio is measured on the PC/UDP side only, not verified RF power"
    };
    private IfAfStatus? previousIfAfStatus;
    private DateTime lastIfAfClipUtc;
    private readonly CancellationTokenSource startupDataCancellation = new();
    private bool initialPassRebuildPending;
    private bool startupReady;
    private Label? startupOverlay;
    private long hamPassGeneration;
    private CancellationTokenSource? hamPassCancellation;
    private static readonly SemaphoreSlim HamPassWorker = new(1, 1);
    private CancellationTokenSource? groupPassCancellation;
    private long groupPassGeneration;
    private static readonly SemaphoreSlim GroupPassWorker = new(1, 1);
    private sealed record GroupPassCacheEntry(
      DateTime CachedUtc, SatnogsDbSatellite[] Sources,
      SatelliteTracker[] Trackers, SatellitePasses.FullRebuildSnapshot Snapshot);
    private readonly Dictionary<string, GroupPassCacheEntry> groupPassCache = new();
    // The identity of the snapshot currently presented by GroupPasses.
    // Keep that snapshot visible while refreshing the SAME group.
    private string? displayedPassGroupId;

    internal static bool CanRetainGroupPassesDuringRefresh(
      string? displayedGroupId, string requestedGroupId) =>
      displayedGroupId == null ||
      string.Equals(displayedGroupId, requestedGroupId, StringComparison.Ordinal);
    private long recentIfAfClips, recentIfAfOutputSamples;
    internal void ApplyEmbeddedSkyCatSettings() =>
      EmbeddedSkyCat.Apply(ctx.Settings.Cat.Embedded);
    internal bool EmbeddedSkyCatRunning => EmbeddedSkyCat.IsRunning;
    private readonly ToolStripSeparator ThemeGitHubSeparator =
      new();
    private readonly ToolStripMenuItem ThemeGitHubLightMNU =
      new();
    private readonly ToolStripMenuItem ThemeGitHubDarkMNU =
      new();
    internal readonly ToolStripMenuItem CwConsoleMNU =
      new();
    internal readonly ToolStripMenuItem OnvifCameraMNU = new();

    public MainForm()
    {
      InitializeComponent();
      ConfigureCwConsoleMenuItem();
      ConfigureSstvConsoleMenuItem();
      ConfigureOnvifCameraMenu();
      ConfigureIfAfLinkBar();
      ConfigureIcomDirectLanMeterBar();
      ConfigureGitHubThemeMenuItems();
      AddOrbitSourceMenuItems();
      AddLogArchiveMenuItem();
      AddEmbeddedSkyCatMenuItem();
      EmbeddedSkyCat.ReadinessChanged += (_,_) =>
      {
        if(!IsHandleCreated || IsDisposed || Disposing)return;
        try
        {
          BeginInvoke((Action)(()=>
          {
            if(!IsDisposed && !Disposing)
            {
              if(EmbeddedSkyCatRunning)
                ctx.CatControl.ApplySettings();
              else
                ctx.CatControl.OnEmbeddedSkyCatExited();
            }
          }));
        }
        catch(InvalidOperationException)
        {
          Log.Debug("SkyCAT readiness UI update skipped after form handle shutdown");
        }
      };

      Text = Utils.GetVersionString();
      ctx.MainForm = this;
      Shown += (_, _) => InitializeStartupAsync().DoNotAwait();

      GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

      Rectangle? bounds = Screen.PrimaryScreen?.Bounds;
      Log.Information($"Screen resolution: {bounds?.Width}x{bounds?.Height}");

      ctx.SatelliteSelector = SatelliteSelecionWidget;
      ctx.FrequencyControl = FrequencyWidget;
      ctx.RotatorControl = RotatorWidget;
      ctx.SatellitePhotoWidget = SatellitePhotoWidget;
      SatelliteSelecionWidget.ctx = ctx;
      FrequencyWidget.ctx = ctx;
      FrequencyWidget.AttachTransmitButton(PttHostPanel);
      GainWidget.ctx = ctx;
      Clock.ctx = ctx;
      ctx.Announcer.ctx = ctx;
      ctx.AutoSelector.ctx = ctx;
      ctx.AutoSelector.Initialize();
      ctx.CatControl.ctx = ctx;
      ctx.CatControl.IcomRfGainReadbackReceived +=
        GainWidget.ApplyRadioRfGainReadback;
      ctx.RotatorControl.ctx = ctx;
      SatellitePhotoWidget.ctx = ctx;
      ctx.AmsatStatusLoader.ctx = ctx;
      ctx.UdpStreamSender.ctx = ctx;

      var settingsLoadResult = ctx.Settings.LoadFromFile();
      UpdateCwConsoleMenuText();

      AddRotatorControlCard();

      // SatnogsDb is intentionally created later in LoadSatelliteData(), after
      // the form has entered its Load phase. Do not dereference it here: the
      // Context field is still null while MainForm itself is being constructed.
      // LoadSatelliteData() configures the newly-created instance with these
      // OrbitSources immediately after construction.
      ApplyThemeSettings();

      ReportSettingsLoadResult(settingsLoadResult);

      EnsureUserDetails();

      ctx.GroupPasses = new(ctx);
      ctx.HamPasses = new(ctx);
      ctx.SdrPasses = new(ctx);
      ctx.LoggerInterface = new(ctx);

      timer.Interval = 1000 / TICKS_PER_SECOND;

      ctx.SpeakerSoundcard.StateChanged += Soundcard_StateChanged;
      ctx.AudioVacSoundcard.StateChanged += Soundcard_StateChanged;
      ctx.IqVacSoundcard.StateChanged += Soundcard_StateChanged;

      ApplyAudioSettings();
      ctx.CwAudio = new CwAudioSourceController(ctx);
      ctx.CwAudio.ApplySettings();
      ctx.CwReceiveWorker =
        new CwReceiveWorker(ctx.CwAudio.Ingress,
          new CwReceiveWorkerOptions
          {
            DecoderMode=ctx.Settings.CwConsole.DecoderMode
          });
      ctx.CwReceiveWorker.Start();
      ctx.CwTransmit =
        new CwTransmitController(
          ctx.Settings.CwConsole,
          new CwOperatorTransmitInterlock());
      ApplyOutputStreamSettings();
      ctx.IfAf = new IfAfService();
      ApplyIfAfSettings();
      ApplyKissServerSettings();
      ctx.CatControl.ApplySettings();
      GainWidget.ApplyRfGain();
      ctx.RotatorControl.ApplySettings();
      ctx.PttHotkey = new PttHotkeyController(ctx);

      ConfigureToolbarScaleMenu();
      ConfigureDockScaleMenu();
      toolbarScale = new UniformControlScale(Toolbar);
      FormClosed += (_, _) =>
      {
        toolbarScale?.Dispose();
        genericRotatorPopupScale?.Dispose();
        yaesuRotatorPopupScale?.Dispose();
        foreach (var scaler in dockScalers.Values) scaler.Dispose();
        dockScalers.Clear();
        registeredDockPanels.Clear();
      };
      ApplyRotatorPopupScale();
      UpdateSatellitePhotoVisibility();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
      // Cancel any held rotator direction through the existing Closed/STOP
      // callback before changing the physical size of its hit targets.
      RotatorControlPopup?.Close(ToolStripDropDownCloseReason.AppClicked);
      YaesuG5500ControlPopup?.Close(ToolStripDropDownCloseReason.AppClicked);
      toolbarScaling = true;
      try
      {
        toolbarScale?.Apply(1f);
        foreach (var scaler in dockScalers.Values) scaler.Apply(1f);
        genericRotatorPopupScale?.Apply(1f);
        yaesuRotatorPopupScale?.Apply(1f);
        base.OnDpiChanged(e);
      }
      finally { toolbarScaling = false; }

      if (IsHandleCreated && !IsDisposed && !Disposing)
        BeginInvoke((Action)RebasePresentationAfterDpiChanged);
    }

    private void RebasePresentationAfterDpiChanged()
    {
      if (IsDisposed || Disposing) return;
      var previousToolbar = toolbarScale;
      toolbarScale = new UniformControlScale(Toolbar);
      previousToolbar?.Dispose();
      foreach (var scaler in dockScalers.Values) scaler.Dispose();
      dockScalers.Clear();
      foreach (var panel in registeredDockPanels.ToArray())
        if (!panel.IsDisposed) ApplyDockScale(panel);
      genericRotatorPopupScale?.Dispose();
      yaesuRotatorPopupScale?.Dispose();
      genericRotatorPopupScale = null;
      yaesuRotatorPopupScale = null;
      ApplyRotatorPopupScale();
      UpdateSatellitePhotoVisibility();
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
      // The message pump must reach its first paint before Orekit/IKVM,
      // cached JSON deserialization or first-run network downloads begin.
      ctx.Settings.Ui.RestoreWindowPosition(this);
      Utils.EnsureFormVisible(this);
      Clock.UtcMode = ctx.Settings.Ui.ClockUtcMode;

      startupOverlay = new Label
      {
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window,
        ForeColor = SystemColors.ControlText,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Initializing orbit engine and satellite cache…"
      };
      Controls.Add(startupOverlay);
      startupOverlay.BringToFront();
    }

    private async Task InitializeStartupAsync()
    {
      // Shown occurs before the first completed render; yield once so the
      // loading surface itself is painted and remains movable/responsive.
      await Task.Delay(80);
      if (IsDisposed || Disposing || startupDataCancellation.IsCancellationRequested)
        return;

      var timer = Stopwatch.StartNew();
      try
      {
        var sources = ctx.Settings.OrbitSources;
        var groupIds = ctx.Settings.Satellites.SatelliteGroups
          .SelectMany(group => group.SatelliteIds).Distinct().ToArray();
        var prepared = await Task.Run(
          () => PrepareSatelliteDatabase(sources, groupIds),
          startupDataCancellation.Token);

        if (IsDisposed || Disposing || startupDataCancellation.IsCancellationRequested)
          return;

        ctx.SatnogsDb = prepared.Database;
        ctx.SatnogsDb.ListUpdated += SatnogsDb_ListUpdated;
        ctx.SatnogsDb.TleUpdated += SatnogsDb_TleUpdated;
        ctx.Settings.Satellites.EnsureSolarSystemGroup(ctx.SatnogsDb);

        if (prepared.OrbitError is Exception orbitError)
        {
          Log.Error(orbitError, "Orekit orbit backend could not be initialized.");
          MessageBox.Show(
            "Orekit initialization failed; orbit tracking will remain unavailable until the data or installation is repaired.\r\n\r\n" +
            orbitError.GetBaseException().Message,
            "Orbit Engine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        initialPassRebuildPending = true;
        if (ctx.SatnogsDb.Loaded)
          SatnogsDb_ListUpdated(null, EventArgs.Empty);
        else
        {
          // A clean install must populate the initial database before docking
          // panels can bind to satellite selections. Keep the modal download
          // progress dialog responsive and parse its data on worker threads.
          DownloadSatList();
          if (!ctx.SatnogsDb.Loaded)
          {
            Close();
            return;
          }
        }

        RestoreStartupLayout(timer.ElapsedMilliseconds);
      }
      catch (OperationCanceledException) when (startupDataCancellation.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Background satellite startup initialization failed.");
        if (!IsDisposed && !Disposing)
        {
          MessageBox.Show(ex.GetBaseException().Message,
            "Satellite initialization", MessageBoxButtons.OK, MessageBoxIcon.Error);
          Close();
        }
        return;
      }
      finally
      {
        if (!IsDisposed && !Disposing)
        {
          startupOverlay?.Dispose();
          startupOverlay = null;
        }
      }

      await CompleteDeferredStartupAsync();
    }

    private static (SatnogsDb Database, Exception? OrbitError) PrepareSatelliteDatabase(
      OrbitSourceSettings sources, string[] groupIds)
    {
      Exception? orbitError = null;
      try { OrbitBackendRegistry.Configure(sources); }
      catch (Exception ex) { orbitError = ex; }

      var database = new SatnogsDb();
      database.ConfigureSources(sources);
      database.LoadFromFile();
      database.ConfigureSolarSystem(sources);
      foreach (string id in groupIds)
        _ = database.GetSatellite(id)?.Tracker.Enabled;
      return (database, orbitError);
    }

    private void RestoreStartupLayout(long elapsedMs)
    {
      ctx.Settings.Ui.RestoreDockingLayout(this);
      Log.Information(
        "Initial SkyRoof docking layout restored in {ElapsedMs} ms; Orekit and satellite cache initialized off UI thread",
        elapsedMs);

      ctx.AutoSelector.RestoreEnabledIfRequested();
      if (EmbeddedSkyCatRunning)
        ctx.CatControl.ApplySettings();
      StartSdrIfEnabled();

      VersionChecker = new VersionChecker(ctx.Settings.LatestVersion);
      VersionChecker.UpdateAvailable += UpdateAvailable_handler;
      VersionChecker.CheckVersionAsync().DoNotAwait();
      ctx.AmsatStatusLoader.GetStatusesAsync().DoNotAwait();
      startupReady = true;
    }

    private async Task CompleteDeferredStartupAsync()
    {
      // Shown fires before the first complete paint. Allow the docking layout
      // to render before scheduling any network maintenance or database refresh.
      await Task.Delay(150);
      if (IsDisposed || Disposing || startupDataCancellation.IsCancellationRequested) return;

      if (initialPassRebuildPending)
      {
        initialPassRebuildPending = false;
        QueueHamPassRebuild();
        QueueGroupPassRebuild();
        ctx.SdrPasses.FullRebuild();
        ctx.PassesPanel?.ShowPasses();
        ctx.GroupViewPanel?.LoadGroup();
        ctx.SkyViewPanel?.ClearPass();
        ctx.QsoSchedulerPanel?.UpdatePredictions();
      }

      // Keep weekly refresh at startup, not after a potentially long EOP
      // timeout when the operator might already be on-air.
      CheckDownloadSatelliteList();
      if (IsDisposed || Disposing || startupDataCancellation.IsCancellationRequested) return;
      CheckDownloadTle();
      var sources = ctx.Settings.OrbitSources;
      string directory = OrbitBackendRegistry.ResolveDataDirectory(sources.OrekitDataDirectory);
      string eop = Path.Combine(directory, "finals2000A.all");
      string de430 = Path.Combine(directory, "lnxp1990.430");
      DateTime previousEopWrite = File.Exists(eop) ? File.GetLastWriteTimeUtc(eop) : DateTime.MinValue;
      bool hadDe430 = File.Exists(de430);

      try
      {
        // The HTTP client and cached DE430 SHA check execute outside WinForms.
        await Task.Run(
          () => OrekitDataBootstrapper.EnsureAsync(
            sources, cancellationToken: startupDataCancellation.Token),
          startupDataCancellation.Token);
      }
      catch (OperationCanceledException) when (startupDataCancellation.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Deferred Orekit data update failed; continuing with cached ephemerides.");
      }

      if (IsDisposed || Disposing || startupDataCancellation.IsCancellationRequested) return;

      // DE430 may have been absent on the first start. Once it is installed,
      // expose its targets without discarding the existing satellite selection.
      if (!hadDe430 && File.Exists(de430))
      {
        int targets = ctx.SatnogsDb.ConfigureSolarSystem(sources);
        if (targets > 0)
          SatnogsDb_ListUpdated(null, EventArgs.Empty);
      }

      // IERS Earth-orientation tables are cached in the process-wide Orekit
      // context. Do not replace a running tracking context behind CAT/rotator.
      if (File.Exists(eop) && File.GetLastWriteTimeUtc(eop) > previousEopWrite)
      {
        const string restartHint = "IERS EOP updated. Restart SkyRoof to load the refreshed Orekit Earth-orientation data.";
        SatDataStatusLabel.ToolTipText = restartHint;
        SatDataLedLabel.ToolTipText = restartHint;
        Log.Information(restartHint);
      }

    }

    private void UpdateAvailable_handler(object? sender, EventArgs e)
    {
      UpdateLabel.Text = $"Download {ctx.Settings.LatestVersion.Name}";
      UpdateLabel.Visible = true;
    }

    private bool radioShutdownCompleted;
    private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
      startupDataCancellation.Cancel();
      CancelPendingHamPassRebuild();
      CancelPendingGroupPassRebuild();
      // CAT/CW radio release is deliberately asynchronous: an unreachable
      // SkyCAT worker or a stuck USB/serial port must not block WinForms.
      if(!radioShutdownCompleted)
      {
        e.Cancel=true;
        timer.Enabled=false;
        if(ctx.Slicer!=null)ctx.Slicer.Enabled=false;
        ctx.AutoSelector.Shutdown();
        ctx.PttHotkey?.Dispose();
        ctx.PttHotkey=null;
        var cw=ctx.CwTransmit;
        ctx.CwTransmit=null;
        if(cw!=null)
        {
          try{await cw.DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(3));}
          catch(Exception ex)
          {
            Log.Warning(ex,"CW STOP timed out during asynchronous shutdown");
          }
        }

        // Preserve the CAT owner's PTT-release ordering, but perform the
        // potentially 4-second PTT verification and thread shutdown OFF UI.
        try
        {
          await Task.Run(()=>ctx.CatControl.Shutdown())
            .WaitAsync(TimeSpan.FromSeconds(6));
        }
        catch(Exception ex)
        {
          Log.Error(ex,"CAT shutdown timed out: verify actual radio PTT; SkyCAT child will attempt owner cleanup");
        }
        radioShutdownCompleted=true;
        if(!IsDisposed && !Disposing)BeginInvoke((Action)Close);
        return;
      }
      timer.Enabled = false;
      if (ctx.Slicer != null) ctx.Slicer.Enabled = false;

      // stop auto-selection and flush any recording segment in progress (plan §1.8), keeping the
      // WasEnabled flag that the save below persists for the restore option
      ctx.AutoSelector.Shutdown();

      // Release a held physical PTT key before CAT teardown, then dispose
      // the global keyboard hook so no new PTT request can race shutdown.
      ctx.PttHotkey?.Dispose();
      ctx.PttHotkey = null;

      // CW STOP has already been attempted on the asynchronous first
      // FormClosing pass; the SkyCAT worker independently owns fail-safe
      // cleanup if that endpoint becomes unresponsive.

      // CAT ownership release was awaited asynchronously above; now the
      // separate SkyCAT worker may restore CI-V state and close its own port.
      // An unresolved PTT release requires an operator hardware check.
      EmbeddedSkyCat.Dispose();

      // save settings
      ctx.Settings.Ui.StoreDockingLayout(DockHost);
      ctx.ClosePanels();

      ctx.Settings.Ui.StoreWindowPosition(this);
      ctx.Settings.Ui.ClockUtcMode = Clock.UtcMode;
      if (ctx.WaterfallPanel != null)
        ctx.Settings.Waterfall.SplitterDistance = ctx.WaterfallPanel.SplitContainer.SplitterDistance;

      ctx.Settings.SaveToFile();

      ThemeRenderer = null;

      // Stop decode/tracking first: it owns background work over the
      // ingress and may still hold an immutable ONNX snapshot.
      ctx.CwReceiveWorker?.Dispose();
      ctx.CwReceiveWorker = null;

      // Then stop optional Windows capture/loopback readers before tearing
      // down the shared SDR/audio devices they may be observing.
      ctx.CwAudio?.Dispose();
      ctx.CwAudio = null;
      ctx.IfAf?.Dispose();
      ctx.IfAf = null;

      // dispose sdr and dsp
      ctx.Sdr?.Dispose();
      ctx.Slicer?.Dispose();
      ctx.SpeakerSoundcard.Dispose();
      ctx.AudioVacSoundcard?.Dispose();
      ctx.IqVacSoundcard?.Dispose();
      ctx.KissServer.Dispose();
      Fft<Complex32>.SaveWisdom();
    }

    // Settings.LoadFromFile recovers from a damaged settings file on its own. Name that file, so that the
    // settings coming back as they were at the previous shutdown, or as the defaults, does not look like
    // SkyRoof forgetting them for no reason.
    private void ReportSettingsLoadResult(SettingsLoadResult result)
    {
      if (result == SettingsLoadResult.Loaded) return;

      string text = $"The settings file\r\n\r\n{Settings.GetFileName()}\r\n\r\nis damaged and could not be read.\r\n\r\n";

      if (ctx.Settings.DamagedFileName != null)
        text += $"The damaged file was kept as {Path.GetFileName(ctx.Settings.DamagedFileName)}.\r\n\r\n";

      text += result == SettingsLoadResult.RestoredFromBackup
        ? "SkyRoof restored the settings saved at the previous shutdown. Settings changed after that are lost."
        : "No usable backup was found, SkyRoof reverted to the default settings.";

      MessageBox.Show(text, "SkyRoof Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void EnsureUserDetails()
    {
      if (UserDetailsDialog.UserDetailsAvailable(ctx)) return;

      var dialog = new UserDetailsDialog(ctx);
      var rc = dialog.ShowDialog();
      bool ok = rc == DialogResult.OK;

      if (ok)
        ctx.Settings.SaveToFile();
      else
        Environment.Exit(1); // unable to proceed without user details, terminate app
    }

    private void ConfigureDockScaleMenu()
    {
      foreach (int value in new[] { 75, 85, 100, 110, 125 })
      {
        int percent = value;
        var option = new ToolStripMenuItem($"{percent}%") { Tag = percent };
        option.Click += (_, _) =>
        {
          ctx.Settings.Ui.DockScalePercent = percent;
          foreach (var panel in registeredDockPanels.ToArray())
            if (!panel.IsDisposed) ApplyDockScale(panel);
          ApplyRotatorPopupScale();
          ctx.Settings.SaveToFile();
        };
        dockScaleMenu.DropDownItems.Add(option);
      }
      dockScaleMenu.DropDownOpening += (_, _) =>
      {
        foreach (ToolStripMenuItem option in dockScaleMenu.DropDownItems)
          option.Checked = option.Tag is int percent &&
            percent == ctx.Settings.Ui.DockScalePercent;
      };
      toolsToolStripMenuItem.DropDownItems.Add(dockScaleMenu);
    }

    private void ApplyRotatorPopupScale()
    {
      float factor = Math.Clamp(ctx.Settings.Ui.DockScalePercent, 75, 125) / 100f;
      if (Math.Abs(factor - 1f) < .001f &&
          genericRotatorPopupScale == null && yaesuRotatorPopupScale == null)
        return;
      if (RotatorControlCard != null)
      {
        genericRotatorPopupScale ??= new UniformControlScale(RotatorControlCard);
        genericRotatorPopupScale.Apply(factor);
        RotatorControlCard.PresentationScale = factor;
        RotatorControlCard.RefreshState();
      }
      if (YaesuG5500ControlCard != null)
      {
        yaesuRotatorPopupScale ??= new UniformControlScale(YaesuG5500ControlCard);
        yaesuRotatorPopupScale.Apply(factor);
      }
    }

    private void RegisterDockScale(DockContent panel)
    {
      if (!registeredDockPanels.Add(panel)) return;
      panel.Shown += (_, _) => ApplyDockScale(panel);
      panel.FormClosed += (_, _) =>
      {
        registeredDockPanels.Remove(panel);
        if (dockScalers.Remove(panel, out var scaler))
          scaler.Dispose();
      };
    }

    private void ApplyDockScale(DockContent panel)
    {
      if (panel.IsDisposed) return;
      float factor = Math.Clamp(ctx.Settings.Ui.DockScalePercent, 75, 125) / 100f;
      if (!dockScalers.TryGetValue(panel, out var scaler))
      {
        if (Math.Abs(factor - 1f) < .001f) return;
        scaler = new UniformControlScale(panel, scaleRootBounds: false);
        dockScalers.Add(panel, scaler);
      }
      scaler.Apply(factor);
      var minimum = scaler.OriginalMinimumSize;
      panel.MinimumSize = new Size(UniformControlScale.Pixel(minimum.Width, factor),
        UniformControlScale.Pixel(minimum.Height, factor));
      if (panel is PassesPanel passes) passes.UpdateDpiPresentation(factor);
      if (panel is SkyViewPanel sky) sky.UpdateDpiPresentation(factor);
    }

    private void Toolbar_Resize(object sender, EventArgs e)
    {
      UpdateSatellitePhotoVisibility();
    }

    private void ConfigureToolbarScaleMenu()
    {
      foreach (int value in new[] { 75, 85, 100, 110, 125 })
      {
        int percent = value;
        var option = new ToolStripMenuItem($"{percent}%")
        {
          Tag = percent
        };
        option.Click += (_, _) =>
        {
          ctx.Settings.Ui.ToolbarScalePercent = percent;
          UpdateSatellitePhotoVisibility();
          ctx.Settings.SaveToFile();
        };
        toolbarScaleMenu.DropDownItems.Add(option);
      }
      toolbarScaleMenu.DropDownOpening += (_, _) =>
      {
        foreach (ToolStripMenuItem option in toolbarScaleMenu.DropDownItems)
          option.Checked = option.Tag is int value &&
            value == ctx.Settings.Ui.ToolbarScalePercent;
      };
      toolsToolStripMenuItem.DropDownItems.Add(toolbarScaleMenu);
    }




    //----------------------------------------------------------------------------------------------
    //                                        sdr
    //----------------------------------------------------------------------------------------------
    internal SpectrumAnalyzer<Complex32>? WidebandSpectrumAnalyzer;

    public void CreateSpectrumAnalyzer()
    {
      Fft<Complex32>.LoadWisdom(Path.Combine(Utils.GetUserDataFolder(), "wsjtx_wisdom.dat"));

      // Show a wait box while the analyzer (and its FFTW plan) is built.
      WaitBox.Run(() =>
        WidebandSpectrumAnalyzer = new(ctx.WaterfallPanel!.WaterfallControl.SpectraWidth, 6_000_000));
      WidebandSpectrumAnalyzer!.SpectrumAvailable += Spect_SpectrumAvailable;
    }

    public void DestroySpectrumAnalyzer()
    {
      WidebandSpectrumAnalyzer!.SpectrumAvailable -= Spect_SpectrumAvailable;
      WidebandSpectrumAnalyzer.Dispose();
      WidebandSpectrumAnalyzer = null;
    }

    private void Spect_SpectrumAvailable(object? sender, DataEventArgs<float> e)
    {
      ctx.WaterfallPanel?.WaterfallControl?.AppendSpectrum(e.Data);
    }

    private void StartSdrIfEnabled()
    {
      var sdrProperties = ctx.Settings.Sdr.Devices.FirstOrDefault(d => d.Name == ctx.Settings.Sdr.SelectedDeviceName);
      ctx.Sdr = sdrProperties == null ? null : new(sdrProperties);

      if (ctx.Sdr != null)
      {
        ctx.Sdr.StateChanged += Sdr_StateChanged;
        ctx.Sdr.DataAvailable += Sdr_DataAvailable;

        GainWidget.ApplyRfGain();
        ConfigureWaterfall();
        ConfigureSlicer();

        if (ctx.Settings.Sdr.Enabled) ctx.Sdr.Enabled = true;
      }

      UpdateSdrLabel();
    }

    private void StopSdr()
    {
      ctx.Sdr?.Dispose();
      ctx.Sdr = null;
      UpdateSdrLabel();
    }

    private void Sdr_StateChanged(object? sender, EventArgs e)
    {
      BeginInvoke(UpdateSdrLabel);
    }

    private void ToggleSdrEnabled()
    {
      StopSdr();
      ctx.Settings.Sdr.Enabled = !ctx.Settings.Sdr.Enabled;
      StartSdrIfEnabled();
    }

    private void Sdr_DataAvailable(object? sender, DataEventArgs<Complex32> e)
    {
      WidebandSpectrumAnalyzer?.StartProcessing(e);
      ctx.Slicer?.StartProcessing(e);
    }

    internal void ConfigureWaterfall()
    {
      if (ctx.WaterfallPanel == null || ctx.Sdr?.Info == null || WidebandSpectrumAnalyzer == null)
        return;

      SetWaterfallSpeed();
      ctx.WaterfallPanel?.SetPassband();
    }

    private void ConfigureSlicer()
    {
      if (ctx.Slicer != null) ctx.Slicer?.Dispose();

      var rate = ctx.Sdr.Info.SampleRate;
      var mode = ctx.FrequencyControl.RadioLink.DownlinkMode;
      ctx.Slicer = new Slicer(rate, 0, mode);
      ctx.Slicer.AudioDataAvailable += Slicer_AudioDataAvailable;
      ctx.Slicer.IqDataAvailable += Slicer_IqDataAvailable;
      ctx.Slicer.Squelch.Enabled = ctx.Settings.Audio.Squelch;
    }

    private void Slicer_IqDataAvailable(object? sender, DataEventArgs<Complex32> e)
    {
      // Headless pass recorder must continue receiving live IQ while the
      // user plays back an unrelated manual recording.
      ctx.AutoSelector.AddIqSamples(e);
      if (ctx.RecorderPanel?.isPlayingBack == true) return;

      ApplyIqOutputStreamGainAndRoute(e.Data, e.Count);
      ctx.RecorderPanel?.AddIqSamples(e);
      ctx.TelemetryPanel?.ProcessSamples(e);
    }

    private void Slicer_AudioDataAvailable(object? sender, DataEventArgs<float> e)
    {
      ctx.Ft4ConsolePanel?.AddSamplesFromSdr(e);
      ctx.CwAudio?.AddSamplesFromSdr(e);

      // Always deliver *raw* Slicer PCM to headless recording, independent
      // of the playback transport and output stream volume settings.
      ctx.AutoSelector.AddAudioSamples(e);
      if (ctx.RecorderPanel?.isPlayingBack == true) return;

      ctx.SpeakerSoundcard.AddSamples(e.Data, 0, e.Count);
      ctx.RecorderPanel?.AddAudioSamples(e);
      ApplyAudioOutputStreamGainAndRoute(e.Data, e.Count);
    }

    private void ApplyAudioOutputStreamGainAndRoute(float[] data, int count)
    {
      if (!ctx.Settings.OutputStream.Enabled || count <= 0) return;
      float gain = Dsp.FromDb2(ctx.Settings.OutputStream.Gain);
      var streamData = new float[count];
      for (int i = 0; i < count; i++) streamData[i] = data[i] * gain;

      if (ctx.Settings.OutputStream.Type == DataStreamType.AudioToVac)
        ctx.AudioVacSoundcard.AddSamples(streamData, 0, count);
      else if (ctx.Settings.OutputStream.Type == DataStreamType.AudioToUdp)
        ctx.UdpStreamSender.Send(streamData, count);
    }

    private void ApplyIqOutputStreamGainAndRoute(Complex32[] data, int count)
    {
      if (!ctx.Settings.OutputStream.Enabled || count <= 0) return;
      float gain = Dsp.FromDb2(ctx.Settings.OutputStream.Gain);
      var streamData = new Complex32[count];
      for (int i = 0; i < count; i++) streamData[i] = data[i] * gain;

      if (ctx.Settings.OutputStream.Type == DataStreamType.IqToVac)
        ctx.IqVacSoundcard.AddSamples(streamData, 0, count);
      else if (ctx.Settings.OutputStream.Type == DataStreamType.IqToUdp)
        ctx.UdpStreamSender.Send(streamData, count);
    }

    internal void RoutePlaybackAudio(float[] data, int offset, int count)
    {
      if (count <= 0) return;

      ctx.SpeakerSoundcard.AddSamples(data, offset, count);

      // Routing applies its own gain to a detached stream buffer.
      float[] streamData = new float[count];
      Array.Copy(data, offset, streamData, 0, count);
      ApplyAudioOutputStreamGainAndRoute(streamData, count);
    }

    internal void RoutePlaybackIq(Complex32[] data, int offset, int count)
    {
      if (count <= 0) return;

      Complex32[] streamData = new Complex32[count];
      Array.Copy(data, offset, streamData, 0, count);
      ApplyIqOutputStreamGainAndRoute(streamData, count);
    }

    private void UpdateSdrLabel()
    {
      Color color;
      string tooltip;

      if (ctx.Sdr == null || !ctx.Sdr.Enabled)
      {
        color = SystemColors.GrayText;
        if (string.IsNullOrEmpty(ctx.Settings.Sdr.SelectedDeviceName))
          tooltip = "Not configured. Click to configure";
        else
          tooltip = $"{ctx.Settings.Sdr.SelectedDeviceName}   Disabled\nClick to enable";
      }
      else if (ctx.Sdr.IsRunning())
      {
        color = Color.Lime;
        tooltip = $"{ctx.Sdr.Info.Name}   Running\nClick to disable";
      }
      else
      {
        color = Color.Red;
        tooltip = $"{ctx.Settings.Sdr.SelectedDeviceName}   FAILED\nClick to disable";
      }

      SdrLedLabel.ForeColor = color;
      SdrLedLabel.ToolTipText = SdrStatusLabel.ToolTipText = tooltip;
    }

    private void EditSdrDevices()
    {
      StopSdr();

      var dlg = new SdrDevicesDialog(ctx);
      var rc = dlg.ShowDialog();

      if (rc == DialogResult.OK)
        ctx.WaterfallPanel?.WaterfallControl?.Invalidate();

      StartSdrIfEnabled();
    }


    internal void SetWaterfallSpeed()
    {
      bool change = ctx.Settings.Waterfall.Speed != ctx.WaterfallPanel.WaterfallControl.ScrollSpeed;

      if (ctx.Sdr != null)
        WidebandSpectrumAnalyzer.Spectrum.Step = ctx.Sdr.Info.SampleRate / ctx.Settings.Waterfall.Speed;

      if (ctx.WaterfallPanel?.WaterfallControl != null)
      {
        ctx.WaterfallPanel.WaterfallControl.ScrollSpeed = ctx.Settings.Waterfall.Speed;
        if (change) ctx.WaterfallPanel.ClearWaterfall();
      }
    }

    internal void SuggestSdrSettings(SoapySdrDeviceInfo info)
    {
      info.Frequency = 436_500_000;
      info.Gain = info.GainRange.maximum;


      if (info.Name.ToLower().Contains("airspy"))
      {
        info.SampleRate = 6_000_000;
        info.MaxBandwidth = SdrConst.MAX_BANDWIDTH;
      }
      else if (info.Name.ToLower().Contains("rtl"))
      {
        info.SampleRate = 2_500_000;
        info.MaxBandwidth = 1_900_000;
      }
      else if (info.Name.ToLower().Contains("sdrplay"))
      {
        //info.SampleRate = 8_000_000;
        //info.HardwareBandwidth = 5_000_000;
        //info.MaxBandwidth = SdrConst.MAX_BANDWIDTH;

        info.SampleRate = 4_000_000;
        info.HardwareBandwidth = 5_000_000;
        info.MaxBandwidth = SdrConst.MAX_BANDWIDTH;
      }
      else
      {
        info.SampleRate = 4_000_000;
        info.MaxBandwidth = SdrConst.MAX_BANDWIDTH;
      }

      info.ValidateRateAndBandwidth();
    }





    //----------------------------------------------------------------------------------------------
    //                                     soundcards
    //----------------------------------------------------------------------------------------------
    internal void ApplyAudioSettings()
    {
      ctx.SpeakerSoundcard.SetDeviceId(ctx.Settings.Audio.SpeakerSoundcard);
      GainWidget.ApplyAfGain();
      ctx.SpeakerSoundcard.Enabled = ctx.Settings.Audio.SpeakerEnabled;
      if (ctx.Slicer != null) ctx.Slicer.Squelch.Enabled = ctx.Settings.Audio.Squelch;

      // The CW loopback endpoint may intentionally inherit the RS-BA1 render
      // endpoint selected in Audio settings, so re-evaluate it here as well.
      ctx.CwAudio?.ApplySettings();
    }

    internal void ApplyIfAfSettings()
    {
      if (ctx.IfAf == null) return;
      try
      {
        var route = ctx.Settings.IfAf.AudioRoute;
        string? ifAfOutputId = IfAfSettings.ResolveRenderEndpoint(route,
          ctx.Settings.IfAf.VacRenderDeviceId,
          route == IfAfAudioRoute.AutoSignedCable
            ? IfAfSignedCableDiscovery.Detect().RenderId : null);
        if (ctx.Settings.IfAf.Enabled && ctx.Settings.OutputStream.Enabled &&
            ctx.Settings.OutputStream.Type == DataStreamType.AudioToVac &&
            ctx.Settings.OutputStream.Vac == ifAfOutputId)
        {
          // ApplyOutputStreamSettings enables the legacy stream before this
          // preflight. Tear down the IF decoder first: a warning
          // must never leave two active writers on one physical cable.
          ctx.IfAf.Stop();
          UpdateIfAfLinkBar();
          throw new InvalidOperationException(
            "IF-AF and SDR Output Stream cannot share one VAC render endpoint.");
        }
        ctx.IfAf.Apply(ctx.Settings.IfAf, CurrentIfAfMode());
        UpdateIfAfLinkBar();
      }
      catch (Exception ex)
      {
        Serilog.Log.Error(ex, "IF-AF receive route failed");
        MessageBox.Show(this, ex.Message, "IF-AF Receiver",
          MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
      // A shared telemetry source follows the IF receiver when that
      // service is restarted or its underlying USB/LAN source changes.
      ctx.TelemetryPanel?.ApplyAudioSettings();
    }

    private IfAfMode CurrentIfAfMode() =>
      IfAfSettings.Resolve(ctx.Settings.IfAf.Mode,
        ctx.FrequencyControl.CurrentIfAfRadioMode);

    internal void ApplyKissServerSettings()
    {
      var sett = ctx.Settings.Telemetry.KissServer;

      ctx.KissServer.Stop(); 
      if (sett.Enabled) ctx.KissServer.Start(sett.Port);      
    }

    internal void ApplyOutputStreamSettings()
    {
      var sett = ctx.Settings.OutputStream;

      ctx.AudioVacSoundcard.SetDeviceId(sett.Vac);
      ctx.IqVacSoundcard.SetDeviceId(sett.Vac);
      EnableDisableVac();
      if (ctx.IfAf != null) ApplyIfAfSettings();
    }

    private void EnableDisableVac()
    {
      var sett = ctx.Settings.OutputStream;

      ctx.AudioVacSoundcard.Enabled = false;
      ctx.IqVacSoundcard.Enabled = sett.Enabled && sett.Type == DataStreamType.IqToVac;
      ctx.AudioVacSoundcard.Enabled = sett.Enabled && sett.Type == DataStreamType.AudioToVac;

      ctx.AudioVacSoundcard.Volume = 1;
      ctx.IqVacSoundcard.Volume = 1;
    }

    private void Soundcard_StateChanged(object? sender, EventArgs e)
    {
      ShowSoundcardLabels();
    }

    public void ShowSoundcardLabels()
    {
      if (!ctx.SpeakerSoundcard.Enabled)
        SoundcardLedLabel.ForeColor = SystemColors.GrayText;
      else if (!ctx.SpeakerSoundcard.IsRunning)
        SoundcardLedLabel.ForeColor = Color.Red;
      else
        SoundcardLedLabel.ForeColor = Color.Lime;

      if (ctx.Settings.IfAf.Enabled)
        VacLedLabel.ForeColor = ctx.IfAf?.IsRunning == true
          ? Color.Lime : Color.Red;
      else if (!ctx.Settings.OutputStream.Enabled)
        VacLedLabel.ForeColor = SystemColors.GrayText;
      else if (ctx.Settings.OutputStream.Type == DataStreamType.AudioToVac && !ctx.AudioVacSoundcard.IsRunning)
        VacLedLabel.ForeColor = Color.Red;
      else if (ctx.Settings.OutputStream.Type == DataStreamType.IqToVac && !ctx.IqVacSoundcard.IsRunning)
        VacLedLabel.ForeColor = Color.Red;
      else
        VacLedLabel.ForeColor = Color.Lime;

      SoundcardStatusLabel.ToolTipText = ctx.SpeakerSoundcard.GetDisplayName();
      VacStatusLabel.ToolTipText = OutputStreamTooltip();
      if (ctx.Settings.IfAf.Enabled && ctx.IfAf != null)
      {
        var state = ctx.IfAf.Status;
        VacStatusLabel.ToolTipText +=
          $"\nIF→AF {state.Mode}: {(state.Running ? "RUNNING" : "STOPPED")}" +
          $"\nIF: {state.InputSamples}  AF: {state.OutputSamples}" +
          $"\nDropped blocks: {state.DroppedBlocks}; underruns: {state.OutputUnderrunSamples}" +
          $"\n{state.Detail}";
      }
    }

    private string OutputStreamTooltip()
    {
      switch (ctx.Settings.OutputStream.Type)
      {
        case DataStreamType.IqToVac:
          return $"I/Q to {ctx.IqVacSoundcard.GetDisplayName()}";
        case DataStreamType.AudioToVac:
          return $"Audio to {ctx.AudioVacSoundcard.GetDisplayName()}";
        case DataStreamType.IqToUdp:
          return $"I/Q to UDP port {ctx.Settings.OutputStream.UdpPort}";
        case DataStreamType.AudioToUdp:
          return $"Audio to UDP port {ctx.Settings.OutputStream.UdpPort}";
        default:
          return "No output stream";
      }
    }




    //----------------------------------------------------------------------------------------------
    //                                      sat data
    //----------------------------------------------------------------------------------------------
    const int LIST_DOWNLOAD_DAYS = 7;
    const int TLE_DOWNLOAD_DAYS = 1;

    bool DownloadOk = true;
    private bool satelliteListDownloading;
    private bool tleDownloading;

    private void CheckDownloadSatelliteList()
    {
      var nextDownloadTime = ctx.Settings.Satellites.LastDownloadTime.AddDays(LIST_DOWNLOAD_DAYS);

      if (!ctx.SatnogsDb.Loaded || DateTime.UtcNow > nextDownloadTime)
        DownloadSatList();
    }

    private void DownloadSatList()
    {
      if (satelliteListDownloading || tleDownloading) return;
      satelliteListDownloading = true;
      try { DownloadOk = DownloadDialog.Download(this, ctx); }
      finally { satelliteListDownloading = false; }
      ShowSatDataStatus();
    }

    public void CheckDownloadTle()
    {
      if (satelliteListDownloading || tleDownloading) return;
      if (DateTime.UtcNow < ctx.Settings.Satellites.LastTleTime.AddDays(TLE_DOWNLOAD_DAYS)) return;
      DownloadTle().DoNotAwait();
    }

    private async Task<bool> DownloadTle()
    {
      if (satelliteListDownloading || tleDownloading) return false;
      tleDownloading = true;
      try
      {
        SatDataLedLabel.ForeColor = SystemColors.GrayText;
        SatDataStatusLabel.ToolTipText = SatDataLedLabel.ToolTipText =
          "Downloading orbit elements...";

        var sources = ctx.Settings.OrbitSources;
        var original = ctx.SatnogsDb;
        var groupIds = ctx.Settings.Satellites.SatelliteGroups
          .SelectMany(group => group.SatelliteIds).Distinct().ToArray();

        var staging = await Task.Run(() =>
        {
          var db = new SatnogsDb();
          db.ConfigureSources(sources);
          db.LoadFromFile();
          return db;
        });

        // This reads manual operator state only on WinForms; heavy orbit
        // parsing and remote I/O operate entirely on the staging instance.
        staging.CopyActiveManualOrbitOverridesFrom(original, DateTime.UtcNow);
        await Task.Run(() => staging.DownloadTle());
        await Task.Run(() =>
        {
          foreach (string id in groupIds)
            _ = staging.GetSatellite(id)?.Tracker.Enabled;
        });

        if (IsDisposed || Disposing || !ReferenceEquals(original, ctx.SatnogsDb))
          return false;
        original.CommitDownloadedOrbits(staging);

        DownloadOk = true;
        Log.Information("Orbit elements downloaded and committed from worker snapshot");
        return true;
      }
      catch (Exception ex)
      {
        DownloadOk = false;
        Log.Error(ex, "TLE download or staged commit failed");
        if (!IsDisposed && !Disposing) ShowSatDataStatus();
        return false;
      }
      finally
      {
        tleDownloading = false;
      }
    }

    //----------------------------------------------------------------------------------------------
    //                                      menu
    //----------------------------------------------------------------------------------------------
    private void ExitMNU_Click(object sender, EventArgs e)
    {
      Close();
    }

    private void DownloadSatDataMNU_Click(object sender, EventArgs e)
    {
      DownloadSatList();
    }

    private async void DownloadTleMNU_Click(object sender, EventArgs e)
    {
      bool ok = await DownloadTle();
      if (!ok)
        MessageBox.Show("Failed to download orbit-element data",
          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void LoadTleMNU_Click(object sender, EventArgs e)
    {
      var dlg = new OpenFileDialog
      {
        Filter =
          "Orbit Elements (*.csv;*.json;*.txt)|*.csv;*.json;*.txt|" +
          "OMM CSV (*.csv)|*.csv|OMM / SatNOGS JSON (*.json)|*.json|" +
          "TLE Text (*.txt)|*.txt|All Files (*.*)|*.*",
        Title = "Load Manual Orbit Elements (priority for 3 days)"
      };
      if (dlg.ShowDialog() != DialogResult.OK) return;

      ctx.SatnogsDb.LoadTleFromFile(dlg.FileName);
    }

    private void AddOrbitSourceMenuItems()
    {
      var sourceMenu =
        new ToolStripMenuItem("Orbit / TLE &Sources");

      var editSources =
        new ToolStripMenuItem("&Edit Source URLs...");
      editSources.Click += (_, _) =>
        new SettingsDialog(
          ctx,
          "SkyRoof.Settings.OrbitSources")
          .ShowDialog(this);

      var reload =
        new ToolStripMenuItem("&Reload Custom TLE Sources");
      reload.Click += async (_, _) =>
        await ReloadCustomTleSourcesAsync();

      sourceMenu.DropDownItems.Add(editSources);
      sourceMenu.DropDownItems.Add(reload);
      toolsToolStripMenuItem.DropDownItems.Add(
        new ToolStripSeparator());
      toolsToolStripMenuItem.DropDownItems.Add(
        sourceMenu);
    }

    private async Task ReloadCustomTleSourcesAsync()
    {
      try
      {
        int count =
          await ctx.SatnogsDb.LoadCustomTleSourcesAsync(
            ctx.Settings.OrbitSources.CustomTleSources);

        if (count > 0)
          SatnogsDb_ListUpdated(null, EventArgs.Empty);

        MessageBox.Show(
          $"Applied {count} TLE record(s) from custom sources.",
          "Custom TLE Sources",
          MessageBoxButtons.OK,
          MessageBoxIcon.Information);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Custom TLE source reload failed.");
        MessageBox.Show(
          ex.Message,
          "Custom TLE Sources",
          MessageBoxButtons.OK,
          MessageBoxIcon.Error);
      }
    }

    private void ConfigureIfAfLinkBar()
    {
      // Match RX CAT: each IF stage is a native ToolStrip LED + text pair.
      // Avoid the old narrow custom-painted strip and its clipped labels.
      string[] labels = { "IF", "DDC", "AF", "V-MIC" };
      int index = StatusStrip.Items.IndexOf(RxCatLedLabel);
      if (index < 0) index = StatusStrip.Items.Count;
      for (int i = 0; i < labels.Length; i++)
      {
        var led = new ToolStripStatusLabel("n")
        {
          Name = "IfAf" + labels[i].Replace("-", "") + "LedLabel",
          Font = new Font("Webdings", 9F),
          ForeColor = SystemColors.GrayText,
          AutoSize = false,
          Size = new Size(21, 30),
          TextAlign = ContentAlignment.MiddleCenter,
          ToolTipText = labels[i] + " receiver stage"
        };
        var text = new ToolStripStatusLabel(labels[i])
        {
          Name = "IfAf" + labels[i].Replace("-", "") + "StatusLabel",
          Font = new Font("Segoe UI", 10F),
          TextAlign = ContentAlignment.MiddleLeft,
          Margin = new Padding(0, 3, 5, 2)
        };
        led.Click += ShowIfAfLinkDiagnostics;
        text.Click += ShowIfAfLinkDiagnostics;
        ifAfStageLeds[i] = led;
        ifAfStageLabels[i] = text;
        StatusStrip.Items.Insert(index++, led);
        StatusStrip.Items.Insert(index++, text);
      }
      UpdateIfAfLinkBar();
    }

    private void ConfigureIcomDirectLanMeterBar()
    {
      directLanMetersHost = new ToolStripControlHost(directLanMeters)
      {
        Name = "IcomDirectLanMeters",
        AutoSize = false,
        Size = new Size(292, 25),
        // Explicit inset keeps the hosted control entirely inside the
        // 35-px StatusStrip, even with the custom theme renderer.
        Margin = new Padding(0, 3, 0, 2),
        Padding = Padding.Empty,
        ToolTipText = "Icom Direct LAN CI-V meter readback"
      };
      // Preserve the original operator layout: S/PWR/ALC/SWR appear at the
      // back, between Rotator and CPU load. Reserve room from the right so
      // variable CAT/satellite labels do not squeeze meters into overflow.
      directLanMetersHost.Alignment = ToolStripItemAlignment.Right;
      directLanMetersHost.Overflow = ToolStripItemOverflow.Never;
      int position = StatusStrip.Items.IndexOf(CpuLoadlabel);
      int insertAt = position >= 0 ? position : StatusStrip.Items.Count;
      StatusStrip.Items.Insert(insertAt, directLanTxAudioLabel);
      StatusStrip.Items.Insert(insertAt + 1, directLanMetersHost);
      UpdateIcomDirectLanMeterBar();
    }

    internal void UpdateIcomDirectLanMeterBar()
    {
      if (directLanMetersHost == null) return;
      IcomDirectLanMeterSettings meters = ctx.Settings.IcomDirectLan.Meters;
      bool visible = meters.Enabled &&
        (meters.S || meters.Power || meters.Alc || meters.Swr) &&
        ctx.Settings.IcomLanSpectrum.Source == IcomLanSpectrumSource.DirectLan;
      directLanMetersHost.Visible = visible;
      directLanTxAudioLabel.Visible = visible && ctx.Settings.IcomDirectLan.Transmit.Enabled;
      if (!visible) return;

      var tx = ctx.IcomLanSpectrumPanel?.TxAudioStatus ?? default;
      bool catPtt = ctx.CatControl.Tx?.Ptt == true ||
                    ctx.CatControl.Rx?.Ptt == true;
      directLanTxAudioLabel.Text = IcomDirectLanTxAudioDisplay.Format(
        tx, catPtt, DateTime.UtcNow);
      directLanTxAudioLabel.ForeColor = StatusStrip.ForeColor;
      directLanTxAudioLabel.ToolTipText =
        $"PC microphone → LAN UDP audio. Sent {tx.UdpSent} PCM packets in this session; " +
        $"last sent sample level {tx.LastSentDbfs:F1} dBFS. " +
        $"IC-9700 radio CI-V MOD input: {tx.ModSource}. " +
        "TX PCM transmission does not by itself prove the radio used this input or radiated RF.";

      directLanMeters.ForeColor = StatusStrip.ForeColor;
      directLanMeters.BackColor = StatusStrip.BackColor;
      IcomMeterSnapshot snapshot =
        ctx.IcomLanSpectrumPanel?.MeterSnapshot ?? new IcomMeterSnapshot();
      directLanMeters.UpdateMeters(
        IcomMeterPollingPolicy.VisibleForMode(snapshot, catPtt), meters);
      directLanMetersHost.ToolTipText =
        "Direct LAN CI-V meter readback (raw data, not calibrated Watts)\n" +
        directLanMeters.StatusText;
    }

    private void ShowIfAfLinkDiagnostics(object? sender, EventArgs e)
    {
      if (ctx.IfAf == null) return;
      var x = ctx.IfAf.Status;
      MessageBox.Show(this,
        $"Mode: {x.Mode}  Receiver: {(x.Running ? "Running" : "Stopped")}\n" +
        $"IF / AF samples: {x.InputSamples} / {x.OutputSamples}\n" +
        $"Input / output RMS: {x.InputRmsDbfs:F1} / {x.OutputRmsDbfs:F1} dBFS\n" +
        $"Dropped blocks: {x.DroppedBlocks}; output underruns: {x.OutputUnderrunSamples}\n" +
        $"AF clipped samples: {x.ClippedSamples}\n\n" +
        x.VirtualDeviceStatus + "\n" + x.Detail,
        "IF→AF Receiver", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static Color IfAfLedColor(IfAfLinkState state) => state switch
    {
      IfAfLinkState.Ok => Color.Lime,
      IfAfLinkState.Warning => Color.DarkOrange,
      IfAfLinkState.Error => Color.Red,
      _ => SystemColors.GrayText
    };

    private void UpdateIfAfLinkBar()
    {
      if (ifAfStageLeds[0] == null) return;
      bool enabled = ctx.Settings.IfAf.Enabled;
      IfAfStatus? status = ctx.IfAf?.Status;
      DateTime nowUtc = DateTime.UtcNow;
      if (!enabled || (status.HasValue && previousIfAfStatus.HasValue &&
          status.Value.InputSamples < previousIfAfStatus.Value.InputSamples))
      {
        previousIfAfStatus = null; // disabled or capture restarted
        lastIfAfClipUtc = default;
      }
      recentIfAfClips = recentIfAfOutputSamples = 0;
      if (status.HasValue && previousIfAfStatus.HasValue &&
          status.Value.OutputSamples >= previousIfAfStatus.Value.OutputSamples)
      {
        recentIfAfClips = Math.Max(0,
          status.Value.ClippedSamples - previousIfAfStatus.Value.ClippedSamples);
        recentIfAfOutputSamples = Math.Max(0,
          status.Value.OutputSamples - previousIfAfStatus.Value.OutputSamples);
        if (recentIfAfClips > 0) lastIfAfClipUtc = nowUtc;
      }
      // Hold the amber indicator for 2s after a REAL recent overload, not
      // forever after one historical clipped sample.
      bool recentClipWarning = lastIfAfClipUtc != default &&
        (nowUtc - lastIfAfClipUtc).TotalSeconds < 2;
      var indicators = IfAfLinkBar.Evaluate(enabled,
        ctx.Settings.IfAf.AudioRoute, status, previousIfAfStatus,
        nowUtc, recentClipWarning);
      var stages = new[] { indicators.If, indicators.Ddc,
        indicators.Af, indicators.VirtualMic };
      string[] stageNames = { "IF capture", "DDC / demodulator",
        "AF output", "Virtual microphone (paired endpoints)" };
      string detail = status.HasValue
        ? $"Mode: {status.Value.Mode}; IF: {status.Value.InputSamples}; " +
          $"AF: {status.Value.OutputSamples}; dropped: {status.Value.DroppedBlocks}; " +
          $"underrun: {status.Value.OutputUnderrunSamples}; clip: {status.Value.ClippedSamples}\n" +
          $"{status.Value.VirtualDeviceStatus}\n{status.Value.Detail}"
        : "IF→AF receiver not started";

      for (int i = 0; i < stages.Length; i++)
      {
        ifAfStageLeds[i].ForeColor = IfAfLedColor(stages[i]);
        string tip = $"{stageNames[i]}: {stages[i]}\n{detail}";
        ifAfStageLeds[i].ToolTipText = tip;
        ifAfStageLabels[i].ToolTipText = tip;
      }
      previousIfAfStatus = enabled ? status : null;
    }

    private void AddEmbeddedSkyCatMenuItem()
    {
      var item=new ToolStripMenuItem("Embedded SkyCAT Status");
      item.Click += (_,_) =>
      {
        var config=ctx.Settings.Cat.Embedded;
        string message=!config.Enabled ? "Embedded SkyCAT is disabled in CAT settings."
          : EmbeddedSkyCatRunning
            ? $"Embedded SkyCAT worker is active.\nModel: {config.Model}\nSerial: {config.SerialPort}\nMain CAT: 127.0.0.1:{config.CatPort}\nScope: 127.0.0.1:{config.SpectrumPort}\n\nAn active worker does not prove the radio serial port is open; consult Tools > Open Log Archive Folder."
            : $"Embedded SkyCAT is not running.\n{config.Validate() ?? "Inspect the log archive for the startup error."}";
        MessageBox.Show(this,message,"Embedded SkyCAT",MessageBoxButtons.OK,
          config.Enabled && !EmbeddedSkyCatRunning ?
            MessageBoxIcon.Warning : MessageBoxIcon.Information);
      };
      toolsToolStripMenuItem.DropDownItems.Add(item);
    }

    private void AddLogArchiveMenuItem()
    {
      var openLogs = new ToolStripMenuItem("Open &Log Archive Folder");
      openLogs.Click += (_, _) => OpenLogArchiveFolder();
      toolsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
      toolsToolStripMenuItem.DropDownItems.Add(openLogs);
    }

    private void OpenLogArchiveFolder()
    {
      try
      {
        string folder = Utils.GetLogArchiveFolder();
        Directory.CreateDirectory(folder);
        var start = new ProcessStartInfo("explorer.exe")
        {
          UseShellExecute = true
        };
        start.ArgumentList.Add(folder);
        Process.Start(start);
      }
      catch(Exception ex)
      {
        Log.Error(ex, "Unable to open SkyRoof log archive folder");
        MessageBox.Show(this,
          "Unable to open the log archive folder.\n\n" + ex.Message,
          "Log Archive", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }

    private void DataFolderMNU_Click(object sender, EventArgs e)
    {
      Process.Start("explorer.exe", Utils.GetUserDataFolder());
    }

    private void EditGroupsMNU_Click(object sender, EventArgs e)
    {
      // using: ShowDialog does not auto-dispose the form; dispose it deterministically
      using var dlg = new SatelliteGroupsForm();
      dlg.SetList(ctx);
      var rc = dlg.ShowDialog(this);

      if (rc != DialogResult.OK) return;
      ctx.Settings.Satellites.DeleteInvalidData(ctx.SatnogsDb);
      SatelliteSelecionWidget.LoadSatelliteGroups();
    }

    private void GroupViewMNU_Click(object sender, EventArgs e)
    {
      if (ctx.GroupViewPanel == null)
        ShowFloatingPanel(new GroupViewPanel(ctx));
      else
        ctx.GroupViewPanel.Close();
    }

    private void SatelliteDetailsMNU_Click(object sender, EventArgs e)
    {
      if (ctx.SatelliteDetailsPanel == null)
        ShowFloatingPanel(new SatelliteDetailsPanel(ctx));
      else
        ctx.SatelliteDetailsPanel.Close();
    }

    private void TransmittersMNU_Click(object sender, EventArgs e)
    {
      if (ctx.TransmittersPanel == null)
        ShowFloatingPanel(new TransmittersPanel(ctx));
      else
        ctx.TransmittersPanel.Close();
    }

    private void SatellitePassesMNU_Click(object sender, EventArgs e)
    {
      if (ctx.PassesPanel == null)
        ShowFloatingPanel(new PassesPanel(ctx));
      else
        ctx.PassesPanel.Close();
    }

    private void TimelineMNU_Click(object sender, EventArgs e)
    {
      if (ctx.TimelinePanel == null)
        ShowFloatingPanel(new TimelinePanel(ctx));
      else
        ctx.TimelinePanel.Close();
    }

    private void SkyViewMNU_Click(object sender, EventArgs e)
    {
      if (ctx.SkyViewPanel == null)
        ShowFloatingPanel(new SkyViewPanel(ctx));
      else
        ctx.SkyViewPanel.Close();
    }

    private void EarthViewMNU_Click(object sender, EventArgs e)
    {
      if (ctx.EarthViewPanel == null)
        ShowFloatingPanel(new EarthViewPanel(ctx));
      else
        ctx.EarthViewPanel.Close();
    }

    private void FrequencyControlMNU_Click(object sender, EventArgs e)
    {
      if (ctx.FrequencyControlPanel == null)
        ShowFloatingPanel(new FrequencyControlPanel(ctx));
      else
        ctx.FrequencyControlPanel.Close();
    }

    private void ConfigureOnvifCameraMenu()
    {
      OnvifCameraMNU.Text = "ONVIF Camera (GPU)";
      OnvifCameraMNU.Name = "OnvifCameraMNU";
      OnvifCameraMNU.Click += (_, _) =>
      {
        if (ctx.OnvifCameraPanel == null)
          ShowFloatingPanel(new OnvifCameraPanel(ctx));
        else
          ctx.OnvifCameraPanel.Close();
      };
      GroupViewPanelMNU.DropDownItems.Insert(
        GroupViewPanelMNU.DropDownItems.IndexOf(IcomLanSpectrumMNU) + 1,
        OnvifCameraMNU);
    }

    private void IcomLanSpectrumMNU_Click(object sender, EventArgs e)
    {
      if (ctx.IcomLanSpectrumPanel == null)
        ShowFloatingPanel(new IcomLanSpectrumPanel(ctx));
      else
        ctx.IcomLanSpectrumPanel.Close();
    }

    private void WaterfallMNU_Click(object sender, EventArgs e)
    {
      if (ctx.WaterfallPanel == null)
        ShowFloatingPanel(new WaterfallPanel(ctx));
      else
        ctx.WaterfallPanel.Close();
    }

    private void QsoEntryMNU_Click(object sender, EventArgs e)
    {
      if (ctx.QsoEntryPanel == null)
        ShowFloatingPanel(new QsoEntryPanel(ctx));
      else
        ctx.QsoEntryPanel.Close();
    }


    private void Ft4ConsoleMNU_Click(object sender, EventArgs e)
    {
      if (ctx.Ft4ConsolePanel == null)
        ShowFloatingPanel(new Ft4ConsolePanel(ctx));
      else
        ctx.Ft4ConsolePanel.Close();
    }

    private void ConfigureCwConsoleMenuItem()
    {
      CwConsoleMNU.Name = "CwConsoleMNU";
      UpdateCwConsoleMenuText();
      CwConsoleMNU.Click += CwConsoleMNU_Click;

      int ft4Index =
        GroupViewPanelMNU.DropDownItems.IndexOf(
          Ft4ConsoleMNU);
      GroupViewPanelMNU.DropDownItems.Insert(
        ft4Index >= 0
          ? ft4Index + 1
          : GroupViewPanelMNU.DropDownItems.Count,
        CwConsoleMNU);
    }

    internal void UpdateCwConsoleMenuText()
    {
      CwConsoleMNU.Text = "&CW Console (RX / TX)";
      CwConsoleMNU.ToolTipText =
        "Manual CW transmit is available independently of satellite " +
        "transponder selection; SkyCAT/radio CW readiness is checked when sending.";
    }

    private void CwConsoleMNU_Click(
      object? sender,
      EventArgs e)
    {
      if (ctx.CwConsolePanel == null)
        ShowFloatingPanel(
          new CwConsolePanel(ctx));
      else
        ctx.CwConsolePanel.Close();
    }

    private void ConfigureSstvConsoleMenuItem()
    {
      var menu = new ToolStripMenuItem("SSTV RX / TX");
      menu.Name = "SstvConsoleMNU";
      menu.Click += (_, _) =>
      {
        if (ctx.SstvConsolePanel == null)
          ShowFloatingPanel(new SstvConsolePanel(ctx));
        else
          ctx.SstvConsolePanel.Close();
      };
      int position = GroupViewPanelMNU.DropDownItems.IndexOf(CwConsoleMNU);
      GroupViewPanelMNU.DropDownItems.Insert(
        position >= 0 ? position + 1 : GroupViewPanelMNU.DropDownItems.Count, menu);
    }

    private void RecorderMNU_Click(object sender, EventArgs e)
    {
      if (ctx.RecorderPanel == null)
        ShowFloatingPanel(new RecorderPanel(ctx));
      else
        ctx.RecorderPanel.Close();
    }

    private void QsoSchedulerMNU_Click(object sender, EventArgs e)
    {
      if (ctx.QsoSchedulerPanel == null)
        ShowFloatingPanel(new QsoSchedulerPanel(ctx));
      else
        ctx.QsoSchedulerPanel.Close();
    }

    private void TelemetryMNU_Click(object sender, EventArgs e)
    {
      if (ctx.TelemetryPanel == null)
        ShowFloatingPanel(new TelemetryPanel(ctx));
      else
        ctx.TelemetryPanel.Close();
    }

    private void AutoSelectionMNU_Click(object sender, EventArgs e)
    {
      if (ctx.AutoSelectionPanel == null)
        ShowFloatingPanel(new AutoSelectionPanel(ctx));
      else
        ctx.AutoSelectionPanel.Close();
    }

    private void SettingsMNU_Click(object sender, EventArgs e)
    {
      new SettingsDialog(ctx).ShowDialog();
    }

    private void WebsiteMNU_Click(object sender, EventArgs e)
    {
      Process.Start(new ProcessStartInfo("https://ve3nea.github.io/SkyRoof") { UseShellExecute = true });
    }

    private void SupportGroupMNU_Click(object sender, EventArgs e)
    {
      Process.Start(new ProcessStartInfo("https://groups.google.com/g/skyroof") { UseShellExecute = true });
    }

    private void AboutMNU_Click(object sender, EventArgs e)
    {
      new AboutBox().ShowDialog();
    }

    private void SdrDevicesMNU_Click(object sender, EventArgs e)
    {
      EditSdrDevices();
    }

    private async void DownloadAmsatMNU_Click(object sender, EventArgs e)
    {
      ctx.Settings.Amsat.Enabled = true;
      bool ok = await ctx.AmsatStatusLoader.GetStatusesAsync();
      if (!ok)
        MessageBox.Show("Failed to download AMSAT statuses",
          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void ResetWindowLayoutMNU_Click(object sender, EventArgs e)
    {
      ctx.Settings.Ui.ResetDockingLayout(this);
    }

    private void UpdateLabel_Click(object sender, EventArgs e)
    {
      Process.Start(new ProcessStartInfo(ctx.Settings.LatestVersion.Url!) { UseShellExecute = true });
    }

    private void RotatorTrackMNU_CheckedChanged(object sender, EventArgs e)
    {
      RotatorWidget.ToggleTracking();
    }




    //----------------------------------------------------------------------------------------------
    //                                       theme
    //----------------------------------------------------------------------------------------------
    private void ConfigureGitHubThemeMenuItems()
    {
      ThemeGitHubSeparator.Name =
        "ThemeGitHubSeparator";

      ThemeGitHubLightMNU.Name =
        "ThemeGitHubLightMNU";
      ThemeGitHubLightMNU.Text =
        "GitHub &Light";
      ThemeGitHubLightMNU.Click +=
        ThemeGitHubLightMNU_Click;

      ThemeGitHubDarkMNU.Name =
        "ThemeGitHubDarkMNU";
      ThemeGitHubDarkMNU.Text =
        "GitHub D&ark";
      ThemeGitHubDarkMNU.Click +=
        ThemeGitHubDarkMNU_Click;

      ThemeMNU.DropDownItems.Add(
        ThemeGitHubSeparator);
      ThemeMNU.DropDownItems.Add(
        ThemeGitHubLightMNU);
      ThemeMNU.DropDownItems.Add(
        ThemeGitHubDarkMNU);
    }

    private void ApplyThemeSettings()
    {
      // The DockPanelSuite theme must be selected before any DockContent is
      // created. Application.SetColorMode has already been called in
      // Program.Main using the same startup ThemeMode.
      switch (Theme.Mode)
      {
        case ThemeMode.GitHubLight:
          ApplyGitHubTheme(
            GitHubThemeColors.Light,
            new GitHubLightDockTheme());
          break;

        case ThemeMode.GitHubDark:
          ApplyGitHubTheme(
            GitHubThemeColors.Dark,
            new GitHubDarkDockTheme());
          break;

        case ThemeMode.Dark:
          DockHost.Theme =
            new VS2015DarkTheme();
          break;

        case ThemeMode.Light:
          ApplyPinkBlueLightTheme();
          break;

        case ThemeMode.System:
        default:
          if (Theme.IsDark)
            DockHost.Theme =
              new VS2015DarkTheme();
          else
            ApplyPinkBlueLightTheme();
          break;
      }

      ShowThemeMenuChecks();
    }

    private void ApplyPinkBlueLightTheme()
    {
      DockHost.Theme =
        new PinkBlueLightDockTheme();

      ThemeRenderer =
        new ToolStripProfessionalRenderer(
          new PinkBlueToolStripColorTable())
        {
          RoundedEdges = false
        };

      menuStrip1.Renderer =
        ThemeRenderer;
      StatusStrip.Renderer =
        ThemeRenderer;

      Toolbar.BackColor =
        Theme.BrandWhite;
      menuStrip1.BackColor =
        Theme.BrandWhite;
      menuStrip1.ForeColor =
        Theme.LightInk;
      StatusStrip.BackColor =
        Theme.BrandPink;
      StatusStrip.ForeColor =
        Theme.LightInk;
    }

    private void ApplyGitHubTheme(
      GitHubPalette palette,
      ThemeBase dockTheme)
    {
      DockHost.Theme =
        dockTheme;

      ThemeRenderer =
        new GitHubToolStripRenderer(
          palette);

      menuStrip1.Renderer =
        ThemeRenderer;
      StatusStrip.Renderer =
        ThemeRenderer;

      Toolbar.BackColor =
        palette.CanvasSubtle;
      menuStrip1.BackColor =
        palette.CanvasInset;
      menuStrip1.ForeColor =
        palette.TextPrimary;
      StatusStrip.BackColor =
        palette.CanvasSubtle;
      StatusStrip.ForeColor =
        palette.TextPrimary;
    }

    private void ShowThemeMenuChecks()
    {
      ThemeMode mode =
        ctx.Settings.Ui.Theme;

      ThemeSystemMNU.Checked =
        mode == ThemeMode.System;
      ThemeLightMNU.Checked =
        mode == ThemeMode.Light;
      ThemeDarkMNU.Checked =
        mode == ThemeMode.Dark;
      ThemeGitHubLightMNU.Checked =
        mode == ThemeMode.GitHubLight;
      ThemeGitHubDarkMNU.Checked =
        mode == ThemeMode.GitHubDark;
    }

    private void ThemeSystemMNU_Click(
      object sender,
      EventArgs e)
    {
      SelectTheme(
        ThemeMode.System);
    }

    private void ThemeLightMNU_Click(
      object sender,
      EventArgs e)
    {
      SelectTheme(
        ThemeMode.Light);
    }

    private void ThemeDarkMNU_Click(
      object sender,
      EventArgs e)
    {
      SelectTheme(
        ThemeMode.Dark);
    }

    private void ThemeGitHubLightMNU_Click(
      object? sender,
      EventArgs e)
    {
      SelectTheme(
        ThemeMode.GitHubLight);
    }

    private void ThemeGitHubDarkMNU_Click(
      object? sender,
      EventArgs e)
    {
      SelectTheme(
        ThemeMode.GitHubDark);
    }

    private void SelectTheme(
      ThemeMode mode)
    {
      if (mode ==
          ctx.Settings.Ui.Theme)
        return;

      ctx.Settings.Ui.Theme =
        mode;
      ctx.Settings.SaveToFile();
      ShowThemeMenuChecks();

      MessageBox.Show(
        "The new theme will be applied the next time SkyRoof starts.",
        "Theme",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
    }



    //----------------------------------------------------------------------------------------------
    //                                     statusbar
    //----------------------------------------------------------------------------------------------
    public void ShowSatDataStatus()
    {
      var sett = ctx.Settings.Satellites;
      bool upToDate =
        sett.LastDownloadTime < DateTime.UtcNow.AddDays(-LIST_DOWNLOAD_DAYS) &&
        sett.LastTleTime < DateTime.UtcNow.AddDays(-TLE_DOWNLOAD_DAYS);

      if (upToDate) SatDataLedLabel.ForeColor = Color.Red;
      else if (!DownloadOk) SatDataLedLabel.ForeColor = Color.Gold;
      else SatDataLedLabel.ForeColor = Color.Lime;

      string tooltip =
        $"Satellite List:  {ClockWidget.Stamp(sett.LastDownloadTime, "yyyy-MM-dd HH:mm")}\n" +
        $"TLE:                 {ClockWidget.Stamp(sett.LastTleTime, "yyyy-MM-dd HH:mm")}" +
        (DownloadOk ? "" : "\nDownload failed");

      SatDataStatusLabel.ToolTipText = SatDataLedLabel.ToolTipText = tooltip;
    }

    DateTime StartTime;
    double StartSeconds;
    private void ShowCpuUsage()
    {
      var time = DateTime.UtcNow;
      var usedSeconds = AppDomain.CurrentDomain.MonitoringTotalProcessorTime.TotalSeconds;
      var usage = (usedSeconds - StartSeconds) / (time - StartTime).TotalSeconds * 100d / Environment.ProcessorCount;
      StartTime = time;
      StartSeconds = usedSeconds;

      CpuLoadlabel.Text = $"    CPU Load: {usage:00.0}%";
    }

    private void SdrStatus_Click(object sender, EventArgs e)
    {
      if (ModifierKeys.HasFlag(Keys.Control) || string.IsNullOrEmpty(ctx.Settings.Sdr.SelectedDeviceName))
        EditSdrDevices();
      else
        ToggleSdrEnabled();
    }

    private void StatusLabel_MouseEnter(object sender, EventArgs e)
    {
      Cursor = Cursors.Hand;
    }

    private void StatusLabel_MouseLeave(object sender, EventArgs e)
    {
      Cursor = Cursors.Default;
    }

    private void SoundcardLabel_Click(object sender, EventArgs e)
    {
      var sett = ctx.Settings.Audio;
      sett.SpeakerEnabled = !sett.SpeakerEnabled;
      ctx.SpeakerSoundcard.Enabled = sett.SpeakerEnabled;
      ShowSoundcardLabels();
    }

    private void VacLabel_Click(object sender, EventArgs e)
    {
      if (ctx.Settings.IfAf.Enabled)
      {
        MessageBox.Show(this,
          "IF→AF 使用独立虚拟声卡输出；请先在设置中关闭 IF→AF，再操作 SDR 输出开关。",
          "IF-AF Receiver", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }
      ctx.Settings.OutputStream.Enabled = !ctx.Settings.OutputStream.Enabled;
      EnableDisableVac();
      ShowSoundcardLabels();
    }

    private void SoundcardDropdownBtn_DropDownOpening(object sender, EventArgs e)
    {
      SoundcardDropdownBtn.DropDownItems.Clear();
      foreach (var dev in OutputSoundcard<float>.ListDevices(DataFlow.Render))
      {
        var item = new ToolStripMenuItem(dev.Name);
        item.Checked = dev.Id == ctx.Settings.Audio.SpeakerSoundcard;

        item.Click += (s, e) =>
        {
          ctx.Settings.Audio.SpeakerSoundcard = dev.Id;
          ApplyAudioSettings();
        };
        SoundcardDropdownBtn.DropDownItems.Add(item);
      }
    }

    private void RxCatLabel_Click(object sender, EventArgs e)
    {
      ctx.Settings.Cat.RxCat.Enabled = !ctx.Settings.Cat.RxCat.Enabled;
      ctx.CatControl.ApplySettings();
      ShowCatStatus();
    }

    private void TxCatLabel_Click(object sender, EventArgs e)
    {
      ctx.Settings.Cat.TxCat.Enabled = !ctx.Settings.Cat.TxCat.Enabled;
      ctx.CatControl.ApplySettings();
      ShowCatStatus();
    }

    private void RotLedLabel_Click(object sender, EventArgs e)
    {
      // The status labels retain their existing left-click enable/disable
      // shortcut. Right-click belongs to the manual control card and must not
      // accidentally toggle the rotator engine.
      bool statusLabelClick =
        ReferenceEquals(sender, RotatorLedLabel) ||
        ReferenceEquals(sender, RotatorStatusLabel);

      if (statusLabelClick &&
          LastRotatorStatusMouseButton == MouseButtons.Right)
      {
        LastRotatorStatusMouseButton = MouseButtons.None;
        return;
      }

      LastRotatorStatusMouseButton = MouseButtons.None;
      ctx.Settings.Rotator.Enabled = !ctx.Settings.Rotator.Enabled;
      ctx.RotatorControl.ApplySettings();
    }


    public void ShowCatStatus()
    {
      bool rxOutOfBand = ctx.FrequencyControl.IsRxCatTransverterOutOfBand;
      bool txOutOfBand = ctx.FrequencyControl.IsTxCatTransverterOutOfBand;

      if (ctx.CatControl.Rx == null) RxCatLedLabel.ForeColor = SystemColors.GrayText;
      else if (!ctx.CatControl.Rx!.IsRunning) RxCatLedLabel.ForeColor = Color.Red;
      else if (rxOutOfBand) RxCatLedLabel.ForeColor = Color.Yellow;
      else RxCatLedLabel.ForeColor = Color.Lime;

      if (!ctx.Settings.Cat.TxCat.Enabled) TxCatLedLabel.ForeColor = SystemColors.GrayText;
      else if (!ctx.FrequencyControl.RadioLink.HasUplink) TxCatLedLabel.ForeColor = SystemColors.ControlText;
      else if (!ctx.CatControl.Tx?.IsRunning ?? false) TxCatLedLabel.ForeColor = Color.Red;
      else if (txOutOfBand) TxCatLedLabel.ForeColor = Color.Yellow;
      else TxCatLedLabel.ForeColor = Color.Lime;

      string rxTip = rxOutOfBand
        ? ctx.FrequencyControl.GetRxCatTransverterOutOfBandMessage()
        : ctx.CatControl.Rx?.GetStatusString() ?? "Disabled";
      RxCatStatusLabel.ToolTipText = rxTip;
      RxCatLedLabel.ToolTipText = rxTip;

      string txTip;
      if (!ctx.Settings.Cat.TxCat.Enabled)
        txTip = "Disabled";
      else if (!ctx.FrequencyControl.RadioLink.HasUplink)
        txTip = "No Uplink";
      else if (txOutOfBand)
        txTip = ctx.FrequencyControl.GetTxCatTransverterOutOfBandMessage();
      else
        txTip = ctx.CatControl.Tx?.GetStatusString();
      TxCatStatusLabel.ToolTipText = txTip;
      TxCatLedLabel.ToolTipText = txTip;
    }

    private void AddRotatorControlCard()
    {
      RotatorControlCard = new RotatorControlCard();
      RotatorControlCard.Attach(
        ctx,
        ctx.RotatorControl);

      RotatorControlHost =
        new ToolStripControlHost(
          RotatorControlCard)
        {
          AutoSize = false,
          Margin = Padding.Empty,
          Padding = Padding.Empty,
          Size = RotatorControlCard.Size
        };

      // The directional card is a dedicated right-click popup. Do not insert
      // it into RotatorDropdownBtn: that arrow intentionally keeps the compact
      // legacy Enable / Track menu.
      RotatorControlPopup =
        new ToolStripDropDown
        {
          AutoSize = false,
          AutoClose = true,
          DropShadowEnabled = true,
          Margin = Padding.Empty,
          Padding = Padding.Empty,
          Size =
            new Size(
              RotatorControlCard.Width + 2,
              RotatorControlCard.Height + 2)
        };
      RotatorControlPopup.Items.Add(
        RotatorControlHost);

      YaesuG5500ControlCard = new YaesuG5500ControlCard();
      YaesuG5500ControlCard.Attach(ctx, ctx.RotatorControl);
      YaesuG5500ControlHost = new ToolStripControlHost(YaesuG5500ControlCard)
      {
        AutoSize = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        Size = YaesuG5500ControlCard.Size
      };
      YaesuG5500ControlPopup = new ToolStripDropDown
      {
        AutoSize = false,
        AutoClose = true,
        DropShadowEnabled = true,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        Size = new Size(YaesuG5500ControlCard.Width + 2,
          YaesuG5500ControlCard.Height + 2)
      };
      YaesuG5500ControlPopup.Items.Add(YaesuG5500ControlHost);
      // Releasing the popup must also release any continuous manual motor
      // command (the mouse-up may occur outside the direction wheel).
      YaesuG5500ControlPopup.Closed += (_, _) =>
        YaesuG5500ControlCard.CancelHeldMove();

      RotatorLedLabel.MouseDown += RotatorStatus_MouseDown;
      RotatorStatusLabel.MouseDown += RotatorStatus_MouseDown;
      RotatorDropdownBtn.MouseDown += RotatorStatus_MouseDown;
    }

    private void RotatorStatus_MouseDown(
      object? sender,
      MouseEventArgs e)
    {
      LastRotatorStatusMouseButton = e.Button;

      if (e.Button != MouseButtons.Right)
        return;

      ShowRotatorManualControl(
        Cursor.Position);
    }

    // Configuration-driven view selection; the control engine remains the
    // same GS-232 or Hamlib backend regardless of which panel is displayed.
    internal static bool UsesYaesuG5500Panel(RotatorSettings settings) =>
      settings.HardwareModel == RotatorHardwareModel.YaesuG5500;

    internal void ShowRotatorManualControl(Point screenPoint)
    {
      bool yaesu = UsesYaesuG5500Panel(ctx.Settings.Rotator);

      if (yaesu)
      {
        if (YaesuG5500ControlPopup == null || YaesuG5500ControlCard == null ||
            YaesuG5500ControlHost == null)
          return;

        RotatorControlPopup?.Close(ToolStripDropDownCloseReason.AppClicked);
        YaesuG5500ControlCard.RefreshState();
        YaesuG5500ControlHost.Size = YaesuG5500ControlCard.Size;
        YaesuG5500ControlPopup.Size = new Size(
          YaesuG5500ControlCard.Width + 2,
          YaesuG5500ControlCard.Height + 2);
        Point location = ClampPopupLocation(
          Screen.FromPoint(screenPoint).WorkingArea,
          YaesuG5500ControlPopup.Size, screenPoint);
        if (YaesuG5500ControlPopup.Visible)
          YaesuG5500ControlPopup.Close(ToolStripDropDownCloseReason.AppClicked);
        YaesuG5500ControlPopup.Show(location);
        return;
      }

      if (RotatorControlPopup == null || RotatorControlCard == null ||
          RotatorControlHost == null)
        return;

      YaesuG5500ControlPopup?.Close(ToolStripDropDownCloseReason.AppClicked);
      RotatorControlCard.RefreshState();

      // Keep the RC-3040S extended card and its original layout unchanged.
      RotatorControlHost.Size = RotatorControlCard.Size;
      RotatorControlPopup.Size = new Size(
        RotatorControlCard.Width + 2, RotatorControlCard.Height + 2);

      Point genericLocation = ClampPopupLocation(
        Screen.FromPoint(screenPoint).WorkingArea,
        RotatorControlPopup.Size, screenPoint);

      if (RotatorControlPopup.Visible)
        RotatorControlPopup.Close(ToolStripDropDownCloseReason.AppClicked);
      RotatorControlPopup.Show(genericLocation);
      RotatorControlCard.PopupOpened();
    }

    internal static Point ClampPopupLocation(
      Rectangle workingArea,
      Size popupSize,
      Point requested)
    {
      int maxX =
        Math.Max(
          workingArea.Left,
          workingArea.Right - popupSize.Width);
      int maxY =
        Math.Max(
          workingArea.Top,
          workingArea.Bottom - popupSize.Height);

      return new Point(
        Math.Clamp(
          requested.X,
          workingArea.Left,
          maxX),
        Math.Clamp(
          requested.Y,
          workingArea.Top,
          maxY));
    }

    public void ShowRotatorStatus()
    {
      if (!ctx.Settings.Rotator.Enabled) RotatorLedLabel.ForeColor = SystemColors.GrayText;
      else if (ctx.FrequencyControl.RadioLink.IsTerrestrial) RotatorLedLabel.ForeColor = SystemColors.ControlText;
      else if (ctx.RotatorControl.ManagementReviewRequired &&
               ctx.RotatorControl.NativeTelemetryConnected) RotatorLedLabel.ForeColor = Color.DarkOrange;
      else if (ctx.RotatorControl.IsRunning()) RotatorLedLabel.ForeColor = Color.Lime;
      else RotatorLedLabel.ForeColor = Color.Red;

      EnableRotatorMNU.Checked = ctx.Settings.Rotator.Enabled;
      TrackRotatorMNU.Checked = ctx.RotatorControl.TrackCheckbox.Checked;

      RotatorStatusLabel.ToolTipText = ctx.RotatorControl.GetStatusString();
      RotatorControlCard?.RefreshState();
      if (YaesuG5500ControlPopup?.Visible == true)
        YaesuG5500ControlCard?.RefreshState();
    }




    //----------------------------------------------------------------------------------------------
    //                                     toolbar
    //----------------------------------------------------------------------------------------------
    private void UpdateSatellitePhotoVisibility()
    {
      if (toolbarScale == null || toolbarScaling) return;
      toolbarScaling = true;
      try
      {
        int preference = Math.Clamp(ctx.Settings.Ui.ToolbarScalePercent, 75, 125);
        float preferred = preference / 100f;
        var (factor, showPhoto) = UniformControlScale.FitToolbar(
          Toolbar.ClientSize.Width, toolbarScale.RequiredWidth(),
          toolbarScale.RequiredWidth(
            SatellitePhotoWidget, SatellitePhotoSeparator), preferred);
        SatellitePhotoWidget.Visible = showPhoto;
        SatellitePhotoSeparator.Visible = showPhoto;
        toolbarScale.Apply(factor);
        lastToolbarScale = factor;
      }
      finally { toolbarScaling = false; }
    }

    internal float ToolbarScaleFactor => lastToolbarScale;

    //----------------------------------------------------------------------------------------------
    //                                     docking
    //----------------------------------------------------------------------------------------------
    public void ShowFloatingPanel(DockContent panel)
    {
      RegisterDockScale(panel);
      var rect = panel.Bounds;
      rect.Offset(
        ctx.MainForm.Location.X + (ctx.MainForm.Width - panel.Width) / 2,
        ctx.MainForm.Location.Y + (ctx.MainForm.Size.Height - panel.Height) / 2
        );

      panel.Show(DockHost, rect);
    }

    public IDockContent? MakeDockContentFromPersistString(string persistString)
    {
      DockContent? panel = persistString switch
      {
        "SkyRoof.GroupViewPanel" => new GroupViewPanel(ctx),
        "SkyRoof.SatelliteDetailsPanel" => new SatelliteDetailsPanel(ctx),
        "SkyRoof.TransmittersPanel" => new TransmittersPanel(ctx),
        "SkyRoof.PassesPanel" => new PassesPanel(ctx),
        "SkyRoof.TimelinePanel" => new TimelinePanel(ctx),
        "SkyRoof.SkyViewPanel" => new SkyViewPanel(ctx),
        "SkyRoof.EarthViewPanel" => new EarthViewPanel(ctx),
        "SkyRoof.FrequencyControlPanel" => new FrequencyControlPanel(ctx),
        "SkyRoof.IcomLanSpectrumPanel" => new IcomLanSpectrumPanel(ctx),
        "SkyRoof.OnvifCameraPanel" => new OnvifCameraPanel(ctx),
        "SkyRoof.WaterfallPanel" => new WaterfallPanel(ctx),
        "SkyRoof.QsoEntryPanel" => new QsoEntryPanel(ctx),
        "SkyRoof.Ft4ConsolePanel" => new Ft4ConsolePanel(ctx),
        "SkyRoof.CwConsolePanel" => new CwConsolePanel(ctx),
        "SkyRoof.SstvConsolePanel" => new SstvConsolePanel(ctx),
        "SkyRoof.RecorderPanel" => new RecorderPanel(ctx),
        "SkyRoof.QsoSchedulerPanel" => new QsoSchedulerPanel(ctx),
        "SkyRoof.TelemetryPanel" => new TelemetryPanel(ctx),
        "SkyRoof.AutoSelectionPanel" => new AutoSelectionPanel(ctx),
        _ => null
      };
      if (panel != null) RegisterDockScale(panel);
      return panel;
    }

    //----------------------------------------------------------------------------------------------
    //                                       timer
    //----------------------------------------------------------------------------------------------
    private const int TICKS_PER_SECOND = 8;
    nuint TickCount;
    private VersionChecker VersionChecker;

    private void timer_Tick(object sender, EventArgs e)
    {
      if (!startupReady)
      {
        Clock.ShowTime();
        return;
      }
      ctx.IfAf?.UpdateMode(CurrentIfAfMode());
      if ((TickCount & 3) == 0)
      {
        UpdateIfAfLinkBar();
        UpdateIcomDirectLanMeterBar();
      }
      // 8 Hz (125 ms) ticks
      TickCount += 1;

      EightHzTick();
      if (TickCount % (TICKS_PER_SECOND / 4) == 0) FourHertzTick();
      if (TickCount % TICKS_PER_SECOND == 0) OneSecondTick();
      if (TickCount % (TICKS_PER_SECOND * 60) == 0) OneMinuteTick().DoNotAwait();
      if (TickCount % (TICKS_PER_SECOND * 3600) == 0) OneHourTick();
    }

    private void EightHzTick()
    {
      ctx.EarthViewPanel?.Advance();
    }

    private void FourHertzTick()
    {
      Clock.ShowTime();
      ctx.SkyViewPanel?.Advance();
      ctx.WaterfallPanel?.ScaleControl?.Invalidate();
      FrequencyWidget.ClockTick();
    }

    private void OneSecondTick()
    {
      ctx.GroupViewPanel?.UpdatePassTimes();
      ctx.PassesPanel?.UpdatePassTimes();
      ctx.TimelinePanel?.Advance();
      ctx.Sdr?.Retry();
      ctx.CatControl.Rx?.Retry();
      ctx.CatControl.Tx?.Retry();
      GainWidget.RefreshControlBindings();
      ctx.Announcer.AnnouncePasses();
      ctx.AutoSelector.Tick();
      ctx.AutoSelectionPanel?.UpdateStatus();
      RotatorWidget.Retry();
      RotatorWidget.Advance();
      ctx.QsoEntryPanel?.SetUtc();
      ctx.TelemetryPanel?.UpdateTxStatus();

      ShowCpuUsage();
      if (ctx.Settings.IfAf.Enabled) ShowSoundcardLabels();
    }
    private Task OneMinuteTick()
    {
      // All three schedulers previously performed Orekit propagation on the
      // 8-Hz WinForms timer. Refresh asynchronously and publish snapshots.
      QueueGroupPassRebuild();
      QueueHamPassRebuild();
      ctx.SdrPasses.FullRebuild();
      ctx.PassesPanel?.ShowPasses();
      ctx.WaterfallPanel?.ScaleControl?.BuildLabels();
      return Task.CompletedTask;
    }

    private void OneHourTick()
    {
      CheckDownloadSatelliteList();
      CheckDownloadTle();

      // Manual file imports temporarily outrank CelesTrak for 72 hours.
      // Releasing an expired override here makes the already-persisted
      // automatic fallback active even when no network request is needed.
      ctx.SatnogsDb.ReleaseExpiredManualOrbitPriority(DateTime.UtcNow);

      ctx.AmsatStatusLoader.GetStatusesAsync();
    }




    //----------------------------------------------------------------------------------------------
    //                                  time display mode
    //----------------------------------------------------------------------------------------------
    // called by the clock widget when the user switches between UTC and local time. The panels
    // listed here format their times on every paint, so redrawing them is all that is needed.
    // The telemetry tree is not redrawn: its captions are dated records of what was received
    // and each one carries the marker of the mode it was written in
    internal void RefreshTimeDisplay()
    {
      ctx.PassesPanel?.Invalidate(true);
      ctx.TimelinePanel?.Invalidate(true);
      ctx.QsoSchedulerPanel?.Invalidate(true);
      ctx.GroupViewPanel?.Invalidate(true);
      ctx.SkyViewPanel?.Invalidate(true);
      ctx.RecorderPanel?.Invalidate(true);
    }




    //----------------------------------------------------------------------------------------------
    //                                 change
    //----------------------------------------------------------------------------------------------
    private void SatnogsDb_ListUpdated(object? sender, EventArgs e)
    {
      CancelPendingGroupPassRebuild();
      groupPassCache.Clear();
      if (sender != null)
      {
        ctx.Settings.Satellites.LastDownloadTime = DateTime.UtcNow;
        ctx.Settings.Satellites.LastTleTime = ctx.Settings.Satellites.LastDownloadTime;
        ctx.Settings.SaveToFile();
      }

      ctx.SatnogsDb.Customize(ctx.Settings.Satellites.SatelliteCustomizations);
      ctx.Settings.Satellites.EnsureSolarSystemGroup(ctx.SatnogsDb);
      ctx.Settings.Satellites.DeleteInvalidData(ctx.SatnogsDb);

      SatelliteSelecionWidget.LoadSatelliteGroups();
      SatellitePhotoWidget.SetSatellite(ctx.SatelliteSelector.SelectedSatellite);

      if (!initialPassRebuildPending)
      {
        QueueHamPassRebuild();
        QueueGroupPassRebuild();
        ctx.SdrPasses.FullRebuild();
      }

      ctx.PassesPanel?.ShowPasses();
      ctx.SkyViewPanel?.ClearPass();
      ctx.WaterfallPanel?.ScaleControl?.BuildLabels();
      ctx.QsoSchedulerPanel?.SetSatelliteList();

      ShowSatDataStatus();
    }

    internal void SetLocation()
    {
      // update data
      CancelPendingGroupPassRebuild();
      groupPassCache.Clear();
      ctx.GroupPasses = new(ctx);
      QueueGroupPassRebuild();

      ctx.HamPasses = new(ctx);
      QueueHamPassRebuild();

      ctx.SdrPasses = new(ctx);
      ctx.SdrPasses.FullRebuild();

      ctx.SatelliteSelector.SetSelectedPass(null);
      ctx.WaterfallPanel?.ScaleControl?.BuildLabels();
      ctx.GroupViewPanel?.LoadGroup();
      ctx.PassesPanel?.ShowPasses();
      ctx.EarthViewPanel?.SetGridSquare();
      ctx.SkyViewPanel?.ClearPass();
      ctx.QsoSchedulerPanel?.UpdatePredictions();
    }

    private void SatnogsDb_TleUpdated(object? sender, EventArgs e)
    {
      CancelPendingGroupPassRebuild();
      groupPassCache.Clear();
      ctx.Settings.Satellites.LastTleTime = DateTime.UtcNow;
      ShowSatDataStatus();

      QueueHamPassRebuild();
      QueueGroupPassRebuild();
      ctx.SdrPasses.Rebuild();

      ctx.SatelliteSelector.SetSelectedPass(null);
      ctx.GroupViewPanel?.LoadGroup(); // replace passes attached to sats
      ctx.PassesPanel?.ShowPasses();
      ctx.SkyViewPanel?.ClearPass();
      ctx.WaterfallPanel?.ScaleControl?.BuildLabels();
      ctx.QsoSchedulerPanel?.UpdatePredictions();
    }

    private void SatelliteSelector_SelectedGroupChanged(object sender, EventArgs e)
    {
      ctx.GroupViewPanel?.LoadGroup();
      QueueGroupPassRebuild();
      ctx.PassesPanel?.ShowPasses();
      ctx.SkyViewPanel?.ClearPass();
      ctx.QsoSchedulerPanel?.SetSatelliteList();
    }

    private void CancelPendingHamPassRebuild()
    {
      ++hamPassGeneration;
      hamPassCancellation?.Cancel();
      hamPassCancellation = null;
    }

    private void QueueHamPassRebuild()
    {
      CancelPendingHamPassRebuild();
      var service = ctx.HamPasses;
      var selected = ctx.SatnogsDb.Satellites
        .Where(s => s.Flags.HasFlag(SatelliteFlags.Vhf) ||
                    s.Flags.HasFlag(SatelliteFlags.Uhf))
        .ToArray();
      var cancellation = new CancellationTokenSource();
      hamPassCancellation = cancellation;
      long generation = hamPassGeneration;
      RebuildHamPassesAsync(service, selected, generation, cancellation).DoNotAwait();
    }

    private async Task RebuildHamPassesAsync(
      HamSatellitePasses service, SatnogsDbSatellite[] selected,
      long generation, CancellationTokenSource cancellation)
    {
      try
      {
        var token = cancellation.Token;
        var snapshot = await Task.Run(async () =>
        {
          await HamPassWorker.WaitAsync(token).ConfigureAwait(false);
          try { return service.ComputeFullRebuildSnapshot(selected, token); }
          finally { HamPassWorker.Release(); }
        }, token);

        if (IsDisposed || Disposing || token.IsCancellationRequested ||
            generation != hamPassGeneration ||
            !ReferenceEquals(service, ctx.HamPasses))
          return;

        service.ApplyFullRebuildSnapshot(snapshot);
        ctx.PassesPanel?.ShowPasses();
        ctx.GroupViewPanel?.UpdatePassTimes();
      }
      catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
      catch (Exception ex)
      {
        Log.Error(ex, "Background amateur satellite pass rebuild failed.");
      }
      finally
      {
        if (ReferenceEquals(hamPassCancellation, cancellation))
          hamPassCancellation = null;
        cancellation.Dispose();
      }
    }

    private void CancelPendingGroupPassRebuild()
    {
      ++groupPassGeneration;
      groupPassCancellation?.Cancel();
      groupPassCancellation = null;
    }

    private void QueueGroupPassRebuild()
    {
      CancelPendingGroupPassRebuild();
      var source = ctx.SatelliteSelector.GroupSatellites.ToArray();
      var tracks = source.Select(s => s.Tracker).ToArray();
      string groupId = ctx.Settings.Satellites.SelectedGroupId;
      var service = ctx.GroupPasses;
      if (groupPassCache.TryGetValue(groupId, out var entry) &&
          DateTime.UtcNow - entry.CachedUtc < TimeSpan.FromMinutes(2) &&
          entry.Sources.Length == source.Length &&
          source.Select((s, i) => ReferenceEquals(s, entry.Sources[i]) &&
            ReferenceEquals(tracks[i], entry.Trackers[i])).All(valid => valid))
      {
        service.ApplySelectedGroupSnapshot(entry.Snapshot);
        displayedPassGroupId = groupId;
        ctx.PassesPanel?.ShowPasses();
        return;
      }

      // A full recomputation may take seconds on an Orekit/IKVM worker.
      // Clearing the SAME group's published list here makes the WinForms
      // virtual list empty at every cache expiry (the reported white flash).
      // Retain its last complete snapshot until the replacement is ready.
      // When switching to a different group, however, never show the old
      // group's satellites as if they belonged to the newly selected group.
      if (!CanRetainGroupPassesDuringRefresh(displayedPassGroupId, groupId))
      {
        service.ClearPendingGroupPasses();
        displayedPassGroupId = null;
      }
      var cancellation = new CancellationTokenSource();
      groupPassCancellation = cancellation;
      long generation = groupPassGeneration;
      ComputeGroupPassesAsync(service, groupId, source, tracks,
        generation, cancellation).DoNotAwait();
    }

    private async Task ComputeGroupPassesAsync(
      GroupSatellitePasses service, string groupId,
      SatnogsDbSatellite[] source, SatelliteTracker[] tracks,
      long generation, CancellationTokenSource cancellation)
    {
      try
      {
        var token = cancellation.Token;
        var snapshot = await Task.Run(async () =>
        {
          await GroupPassWorker.WaitAsync(token).ConfigureAwait(false);
          try { return service.ComputeFullRebuildSnapshot(source, token); }
          finally { GroupPassWorker.Release(); }
        }, token);
        if (IsDisposed || Disposing || token.IsCancellationRequested ||
            generation != groupPassGeneration ||
            !ReferenceEquals(service, ctx.GroupPasses) ||
            ctx.Settings.Satellites.SelectedGroupId != groupId)
          return;

        service.ApplySelectedGroupSnapshot(snapshot);
        displayedPassGroupId = groupId;
        groupPassCache[groupId] = new GroupPassCacheEntry(
          DateTime.UtcNow, source, tracks, snapshot);
        ctx.PassesPanel?.ShowPasses();
        ctx.QsoSchedulerPanel?.UpdatePredictions();
      }
      catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
      catch (Exception ex)
      {
        Log.Error(ex, "Asynchronous Orekit group pass prediction failed.");
      }
      finally
      {
        if (ReferenceEquals(groupPassCancellation, cancellation))
          groupPassCancellation = null;
        cancellation.Dispose();
      }
    }

    private void SatelliteSelector_SelectedSatelliteChanged(object sender, EventArgs e)
    {
      ctx.GroupViewPanel?.ShowSelectedSat();
      ctx.EarthViewPanel?.SetSatellite();
      ctx.SatelliteDetailsPanel?.SetSatellite();
      ctx.TransmittersPanel?.SetSatellite();
      ctx.PassesPanel?.ShowPasses();
      ctx.QsoEntryPanel?.SetSatellite();
      SatellitePhotoWidget.SetSatellite(ctx.SatelliteSelector.SelectedSatellite);
    }

    private void SatelliteSelector_SelectedTransmitterChanged(object sender, EventArgs e)
    {
      // JPL Moon/Sun/Venus and custom tracking-only objects intentionally have
      // no fake radio transmitter. Selecting one must not retune CAT/SDR.
      if (ctx.SatelliteSelector.SelectedTransmitter == null)
      {
        ctx.TransmittersPanel?.ShowSelectedTransmitter();
        ctx.WaterfallPanel?.ScaleControl?.BuildLabels();
        return;
      }

      FrequencyWidget.SetTransmitter(returnToBase: true);
      ctx.TransmittersPanel?.ShowSelectedTransmitter();
      ctx.WaterfallPanel?.BringInView(ctx.FrequencyControl.RadioLink.CorrectedDownlinkFrequency);
      ctx.SdrPasses.UpdateFrequencyRange();
      ctx.WaterfallPanel?.ScaleControl?.BuildLabels();
      ctx.QsoEntryPanel?.SetBand();
      ctx.QsoEntryPanel?.SetMode();
      ctx.RecorderPanel?.RememberRecordingEvents();
      ctx.TelemetryPanel?.SetTransmitter();
    }

    private void SatelliteSelector_SelectedPassChanged(object sender, EventArgs e)
    {
      SatellitePass? pass = ctx.SatelliteSelector.SelectedPass;
      ctx.SkyViewPanel?.SetPass(pass);
      ctx.EarthViewPanel?.SetPass(pass);
      // Idle-gap Auto Selection pre-tunes the radio, not the antenna. A
      // manual tracking pass must survive the satellite selector's events.
      // Normal selections and schedule-owned TrackPass at AOS are unchanged.
      if (!ctx.AutoSelector.IsPreselecting || !ctx.RotatorControl.IsTracking)
        RotatorWidget.SetPass(pass);
    }
  }
}
