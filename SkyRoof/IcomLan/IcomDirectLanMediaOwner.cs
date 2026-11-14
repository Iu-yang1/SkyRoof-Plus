using SkyRoof.IF;

namespace SkyRoof
{
  /// <summary>
  /// Owns the Direct LAN control/CI-V/audio session independently of a
  /// DockPanel spectrum window. Closing or pausing the visual scope must
  /// never silently dispose the radio-to-PC / PC-to-radio audio session.
  /// </summary>
  internal sealed class IcomDirectLanMediaOwner : IDisposable
  {
    private readonly Context ctx;
    private IcomLanSpectrumCapture? capture;
    private IcomDirectLanAudioDevices? devices;
    private IcomMeterSnapshot meters = new();
    private readonly object meterSync = new();

    internal IcomDirectLanMediaOwner(Context ctx) => this.ctx = ctx;
    internal IcomLanSpectrumCapture? Capture => capture;
    internal bool IsActive => capture != null;
    internal IcomDirectLanTxAudioStatus TxAudioStatus =>
      capture?.TxAudioStatus ?? new IcomDirectLanTxAudioStatus(
        false, ctx.Settings.IcomDirectLan.Transmit.Enabled, 0, -120, 0);
    internal IcomMeterSnapshot MeterSnapshot
    {
      get { lock (meterSync) return meters; }
    }

    internal static bool PreserveAudioOnScopeStop(
      IcomLanSpectrumSource source, IcomDirectLanSettings settings) =>
      source == IcomLanSpectrumSource.DirectLan &&
      (settings.Receive.Enabled || settings.Transmit.Enabled);

    internal IcomLanSpectrumCapture GetOrStart()
    {
      if (capture != null) return capture;

      IcomLanSpectrumSettings settings = ctx.Settings.IcomLanSpectrum;
      IcomDirectLanSettings audio = ctx.Settings.IcomDirectLan;
      var current = new IcomLanSpectrumCapture(
        settings.RadioAddress,
        settings.SerialPort,
        useDirectLan: true,
        directLanControlPort: settings.DirectLanControlPort,
        directLanUsername: settings.DirectLanUsername,
        directLanPassword: settings.DirectLanPassword,
        directLanClientName: settings.DirectLanClientName,
        radioModel: settings.RadioModel,
        directLanFastSweep: settings.DirectLanFastSweep,
        directLanAudioSettings: audio,
        directLanPttActive: () =>
          ctx.CatControl.Tx?.Ptt == true || ctx.CatControl.Rx?.Ptt == true,
        directLanExternalMeterReadback: () =>
          ctx.CatControl.HasIcomMeterReadbackBackend &&
          ctx.IcomLanSpectrumPanel?.HasRecentExternalMeterReadback == true);
      var localAudio = new IcomDirectLanAudioDevices(
        audio, current,
        (rate, frame) => ctx.IfAf?.AcceptNetworkIfAudio(rate, frame));
      current.AudioFrameReceived += OnAudioFrame;
      current.MeterReceived += OnMeter;
      devices = localAudio;
      capture = current;
      try
      {
        localAudio.Start();
        current.Start();
        return current;
      }
      catch
      {
        capture = null;
        devices = null;
        current.AudioFrameReceived -= OnAudioFrame;
        current.MeterReceived -= OnMeter;
        localAudio.Dispose();
        current.Dispose();
        throw;
      }
    }

    private void OnAudioFrame(int rate, IcomLanLpcmAudioCodec.RxFrame frame) =>
      devices?.AcceptReceiveAudio(rate, frame);

    private void OnMeter(IcomMeterReading reading)
    {
      lock (meterSync)
        meters = meters.With(reading, DateTime.UtcNow);
    }

    internal void Stop()
    {
      IcomLanSpectrumCapture? oldCapture = capture;
      IcomDirectLanAudioDevices? oldDevices = devices;
      capture = null;
      devices = null;
      if (oldCapture == null) return;
      oldCapture.AudioFrameReceived -= OnAudioFrame;
      oldCapture.MeterReceived -= OnMeter;
      // The network session must log out before the next authenticated
      // session is created. The capture class additionally serializes all
      // Direct LAN login transactions for this radio.
      oldCapture.Dispose();
      oldDevices?.Dispose();
      lock (meterSync) meters = new IcomMeterSnapshot();
      ctx.IfAf?.NetworkIfDisconnected();
    }

    internal void ReconfigureIfActive()
    {
      if (!IsActive) return;
      Stop();
      if (PreserveAudioOnScopeStop(
            ctx.Settings.IcomLanSpectrum.Source, ctx.Settings.IcomDirectLan))
        GetOrStart();
    }

    public void Dispose() => Stop();
  }
}
