using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public enum OperatingMode { RxOnly, TxOnly, Simplex, Split, Duplex }

  public partial class CatControlEngine : ControlEngine
  {
    const long NOT_ASSIGNED = 0;

    private bool rx, tx, crossband;
    private OperatingMode CatMode;
    private RadioCapabilities RadioCapabilities;
    private AvailableCommands Caps;
    private readonly int TuningStep;
    private readonly int MicrowaveTuningStep;
    private readonly int MicrowaveCycleDelayMs;
    private readonly bool IgnoreDialKnob;

    public bool Ptt { get; private set; } = false;
    private long lastPttReadbackTicks;
    private long lastPttOnReadbackTicks;
    private long lastPttOffReadbackTicks;
    // A CAT write ACK alone is not a hardware PTT readback. SSTV
    // transmission checks this timestamp before starting audio.
    internal DateTime LastPttHardwareReadbackUtc
    {
      get
      {
        long ticks = Interlocked.Read(ref lastPttReadbackTicks);
        return ticks == 0 ? DateTime.MinValue :
          new DateTime(ticks, DateTimeKind.Utc);
      }
    }
    // Keep state-specific hardware evidence. A command-ACK update to Ptt
    // cannot turn the last *opposite-state* CAT poll into a PTT readback.
    internal DateTime LastPttOnHardwareReadbackUtc =>
      ReadPttTimestamp(ref lastPttOnReadbackTicks);
    internal DateTime LastPttOffHardwareReadbackUtc =>
      ReadPttTimestamp(ref lastPttOffReadbackTicks);

    private static DateTime ReadPttTimestamp(ref long ticks)
    {
      long value = Interlocked.Read(ref ticks);
      return value == 0 ? DateTime.MinValue :
        new DateTime(value, DateTimeKind.Utc);
    }

    private bool PttChanged = false;

    // Ownership is deliberately separate from the observed PTT state. A second
    // client (for example WSJT-X) may key the same radio; shutting down SkyRoof
    // must only release PTT that SkyRoof itself successfully asserted.
    private volatile bool PttOwnedByApplication;
    // A PTT-ON write can time out after the radio has already acted. Keep the
    // attempt separate from confirmed ownership so shutdown/key release can
    // still fail safe until hardware readback resolves the ambiguity.
    private volatile bool PttOnAttempted;
    internal bool PttMayBeOwnedByApplication =>
      PttOwnedByApplication || PttOnAttempted;

    private readonly ManualResetEventSlim PttReleased = new(true);
    private bool DialKnobSpinning = false;
    public long RequestedRxFrequency, LastWrittenRxFrequency, LastReadRxFrequency;
    public long RequestedTxFrequency, LastWrittenTxFrequency, LastReadTxFrequency;
    private Slicer.Mode? RequestedRxMode, LastWrittenRxMode;      
    private Slicer.Mode? RequestedTxMode, LastWrittenTxMode;
    private bool? RequestedPtt;
    private readonly object pttRequestSync = new();

    private bool? GetRequestedPtt()
    {
      lock (pttRequestSync) return RequestedPtt;
    }

    // The request may be changed to OFF while a prior ON command is in
    // flight. Never erase that newer STOP request when ON is acknowledged.
    internal static bool? PendingAfterPttAck(bool? pending, bool completedPtt) =>
      pending.HasValue && pending.Value == completedPtt ? null : pending;

    internal static bool ShouldAttemptPtt(bool ctcssReady, bool? requestedPtt) =>
      requestedPtt == false || ctcssReady;

    internal static bool CanAttemptPttWithPreparedTx(
      bool? requestedPtt, bool ctcssReady, bool txPrepared) =>
      requestedPtt == false || (ctcssReady && txPrepared);

    // CTCSS encode state requested by the UI. CtcssEnabled is null until the app sets it,
    // CtcssPending is true while the state still has to be written to the radio
    private double CtcssTone = CtcssTones.DEFAULT_TONE;
    private bool? CtcssEnabled;
    private bool CtcssPending;
    // One-shot guard for transmitter changes. IC-9700/SkyCAT may swap Main/Sub while
    // applying a new cross-band frequency pair; reassert CTCSS only after those tune
    // writes have settled so the encoder ends up on the final TX/Sub side.
    private bool CtcssReassertAfterTune;
    private double? RequestedArmingTone;

    // Ignore Dial Knob affects operator feedback, not AFC's need to read
    // the actual receiver frequency. The CAT worker remains the sole reader.
    internal Func<bool>? AfcReadbackRequired;
    public static bool ShouldReadRxFrequency(bool ignoreDialKnob,
      bool afcReadbackRequired, bool receiverReadable) =>
      receiverReadable && (!ignoreDialKnob || afcReadbackRequired);

    public event Action<long, DateTime>? RxReadbackSampled;
    public event EventHandler? RxTuned;
    public event EventHandler? TxTuned;

    public CatControlEngine(CatRadioSettings radioSettings, CatSettings catSettings) : base(radioSettings.Host, radioSettings.Port, catSettings)
    {
      TuningStep = catSettings.TuningStep;
      MicrowaveTuningStep = catSettings.MicrowaveTuningStep;
      MicrowaveCycleDelayMs = catSettings.MicrowaveCycleDelayMs;
      IgnoreDialKnob = catSettings.IgnoreDialKnob;
    }

    private void LogInfo(string msg)
    {
      if (log) Log.Information(msg);
    }

    private void LogFreqs(string msg)
    {
      LogInfo($"{msg}  (RxReq={RequestedRxFrequency}  RxWr={LastWrittenRxFrequency}  RxRd={LastReadRxFrequency})");
    }

    // some radios use 10 Hz steps and some don't, round frequency to the tuning step
    protected override int CycleDelayMs =>
      SelectCatCycleDelayMs(
        Delay, MicrowaveCycleDelayMs,
        RequestedRxFrequency, RequestedTxFrequency);

    internal static int SelectCatCycleDelayMs(
      int normalDelayMs, int microwaveDelayMs,
      long rxHz, long txHz) =>
      rxHz >= 1_000_000_000L || txHz >= 1_000_000_000L
        ? Math.Min(normalDelayMs, microwaveDelayMs)
        : normalDelayMs;

    private long RoundToStep(double freq) =>
      QuantizeCatFrequency(freq, TuningStep, MicrowaveTuningStep);

    internal static long QuantizeCatFrequency(
      double freq, int legacyStep, int microwaveStep)
    {
      if (!double.IsFinite(freq) || freq < 0 || freq > long.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(freq));

      bool microwave = freq >= 1_000_000_000;
      int step = Math.Max(1, microwave ? microwaveStep : legacyStep);
      double ticks = microwave
        ? Math.Round(freq / step, MidpointRounding.AwayFromZero)
        : Math.Truncate(freq / step);

      return checked((long)ticks * step);
    }

    public void Start(bool rx, bool tx, bool crossband)
    {
      this.rx = rx;
      this.tx = tx;
      this.crossband = crossband;

      StartThread();
    }

    public void SetRxFrequency(double frequency)
    {
      frequency = RoundToStep(frequency);
      LogFreqs($"SetRxFrequency {frequency}");

      if (DialKnobSpinning)
        LogInfo("Ignoring RX frequency change while dial knob is spinning");
      else
        RequestedRxFrequency = (long)frequency;
    }

    public void SetTxFrequency(double frequency)
    {
      frequency = RoundToStep(frequency);
      LogFreqs($"SetTxFrequency {frequency}");

      if (DialKnobSpinning)
        LogInfo("Ignoring TX frequency change while dial knob is spinning");
      else
        RequestedTxFrequency = (long)frequency;
    }

    public void SetRxMode(Slicer.Mode mode)
    {
      RequestedRxMode = mode;
    }

    public void SetTxMode(Slicer.Mode mode)
    {
      RequestedTxMode = mode;
    }

    public void SetPtt(bool ptt)
    {
      LogInfo($"SetPtt {ptt}");
      lock (pttRequestSync) RequestedPtt = ptt;
    }

    internal void ReleaseApplicationPtt()
    {
      // Atomic with the CAT worker's transition from queued ON to in-flight
      // ON. Once an ON attempt has started, STOP must remain queued as OFF
      // even if the original ON reply arrives after cancellation.
      lock (pttRequestSync)
      {
        if (PttMayBeOwnedByApplication)
        {
          LogInfo("ReleaseApplicationPtt: requesting PTT OFF");
          RequestedPtt = false;
        }
        else if (RequestedPtt == true)
        {
          LogInfo("ReleaseApplicationPtt: canceling unsent PTT ON");
          RequestedPtt = null;
        }
      }
    }

    // called whenever the transmitter settings are applied, including on every Doppler update,
    // so the radio is written to only when the tone or the on/off state actually changes
    public void SetCtcssTone(double toneHz, bool enabled)
    {
      if (toneHz == CtcssTone && enabled == CtcssEnabled) return;

      LogInfo($"SetCtcssTone {toneHz} {(enabled ? "on" : "off")}");
      CtcssTone = toneHz;
      CtcssEnabled = enabled;
      CtcssPending = true;
    }

    // Request a one-shot CTCSS write after all currently pending RX/TX frequency and
    // mode changes are complete. This is intentionally separate from CtcssPending:
    // the desired tone may be unchanged while an IC-9700 Main/Sub swap moves the
    // previously configured encoder state to the wrong side.
    public void RequestCtcssReassertAfterTune()
    {
      LogInfo("CTCSS reassert requested after tune");
      CtcssReassertAfterTune = true;
    }

    // one-shot keyed carrier with the given tone, used to arm the SO-50 timer
    public void SendArmingTone(double toneHz)
    {
      LogInfo($"SendArmingTone {toneHz}");
      RequestedArmingTone = toneHz;
    }

    public override void Dispose()
    {
      // Cancel a not-yet-sent SkyRoof PTT-ON request. If SkyRoof actually keyed
      // the rig, ask the worker to send PTT OFF and give it a bounded opportunity
      // to receive the radio acknowledgement before the TCP connection is torn down.
      bool releasePtt;
      lock (pttRequestSync)
      {
        releasePtt = PttMayBeOwnedByApplication;
        if (releasePtt) RequestedPtt = false;
        else if (RequestedPtt == true) RequestedPtt = null;
      }

      if (releasePtt && !PttReleased.Wait(4000))
        Log.Warning(
          "CAT engine stopped while SkyRoof-owned or possibly-owned PTT could not be confirmed OFF.");

      base.Dispose();
      PttReleased.Dispose();
    }

    protected void OnRxFrequencyChanged()
    {
      LogFreqs($"OnRxFrequencyChanged");
      syncContext.Post(s => RxTuned?.Invoke(this, EventArgs.Empty), null);
    }

    protected void OnTxFrequencyChanged()
    {
      LogInfo($"OnTxFrequencyChanged:  (TxRequested={RequestedTxFrequency}  TxWritten={LastWrittenTxFrequency}  TxRdead={LastReadTxFrequency}  RxWritten={LastWrittenRxFrequency}  RxRead={LastReadRxFrequency})");

      syncContext.Post(s => TxTuned?.Invoke(this, EventArgs.Empty), null);
    }
  }
}
