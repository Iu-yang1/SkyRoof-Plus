using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public partial class CatControlEngine
  {
    internal bool CanPtt()
    {
      return CatMode != OperatingMode.RxOnly &&
             commands.set_ptt_on != null &&
             commands.set_ptt_off != null;
    }

    private void ReadPtt()
    {
      PttChanged = false;
      if (commands.read_ptt == null) return;

      var reply = SendReadCommand(commands.read_ptt);
      if (reply != "0" && reply != "1")
      {
        if (reply != null) BadReply(reply);
        return;
      }

      long readbackTicks = DateTime.UtcNow.Ticks;
      Interlocked.Exchange(ref lastPttReadbackTicks, readbackTicks);
      bool newPtt = reply == "1";
      if (newPtt)
        Interlocked.Exchange(ref lastPttOnReadbackTicks, readbackTicks);
      else
        Interlocked.Exchange(ref lastPttOffReadbackTicks, readbackTicks);

      PttChanged = newPtt != Ptt;
      Ptt = newPtt;
      if (PttChanged)
        Log.Information(
          "CAT PTT readback transition: PTT={Ptt}, mode={Mode}, lastRxHz={Rx}, lastTxHz={Tx}, requestedCtcss={CtcssEnabled}, toneHz={Tone:F1}",
          Ptt, CatMode, LastReadRxFrequency, LastReadTxFrequency,
          CtcssEnabled, CtcssTone);

      if (PttChanged)
        if (Ptt == true) LastWrittenTxFrequency = NOT_ASSIGNED; else LastWrittenRxFrequency = NOT_ASSIGNED;

      // A CAT write can time out after the radio already acted on it. Treat a
      // matching hardware read-back as confirmation of our pending request.
      // Conversely, any observed RX state proves there is no SkyRoof-owned PTT
      // left to release.
      if (!Ptt)
      {
        PttOwnedByApplication = false;
        PttOnAttempted = false;
        PttReleased.Set();
      }
      else if (PttOnAttempted)
      {
        // We attempted PTT ON and the next hardware read says TX. Even if the
        // write reply was lost, treat the transmission as ours for fail-safe
        // release purposes.
        PttOwnedByApplication = true;
        PttOnAttempted = false;
        PttReleased.Reset();
      }

      lock (pttRequestSync)
      {
        if (RequestedPtt.HasValue && RequestedPtt.Value == Ptt)
        {
          // A hardware match satisfies the queued request; do not clear a
          // concurrently queued opposite state (notably STOP after PTT ON).
          if (!Ptt)
          {
            PttOwnedByApplication = false;
            PttOnAttempted = false;
            PttReleased.Set();
          }
          RequestedPtt = null;
        }
      }

      LogInfo($"ReadPtt: {Ptt} (changed={PttChanged})");
    }

    private void TryWritePtt(bool ctcssReady, bool txPrepared)
    {
      if (CatMode == OperatingMode.RxOnly) return;
      bool commandPtt;
      string? command;
      lock (pttRequestSync)
      {
        if (!RequestedPtt.HasValue) return;
        commandPtt = RequestedPtt.Value;
        if (!CanAttemptPttWithPreparedTx(commandPtt, ctcssReady, txPrepared)) return;
        if (commandPtt == Ptt)
        {
          // Do not claim ownership of an externally asserted PTT just
          // because a matching state was requested.
          if (!Ptt)
          {
            PttOwnedByApplication = false;
            PttReleased.Set();
          }
          RequestedPtt = null;
          return;
        }

        command = commandPtt ? commands.set_ptt_on : commands.set_ptt_off;
        if (command == null) return;
        if (commandPtt)
        {
          // From this point a concurrent STOP must queue an OFF command;
          // it cannot simply cancel the queued ON as never attempted.
          PttOnAttempted = true;
          PttReleased.Reset();
        }
      }

      // CAT writes may block. Never hold the request lock across I/O: STOP
      // must be able to queue OFF immediately while ON is in flight.
      if (!SendWriteCommand(command)) return;

      lock (pttRequestSync)
      {
        // This is the state of the command we actually sent, NOT the latest
        // requested state. Reading RequestedPtt.Value here used to erase an
        // intervening STOP and could falsely report RX after sending PTT ON.
        Ptt = commandPtt;
        PttChanged = true;
        if (commandPtt)
        {
          PttOwnedByApplication = true;
          PttOnAttempted = false;
          PttReleased.Reset();
          LastWrittenTxFrequency = NOT_ASSIGNED;
        }
        else
        {
          PttOwnedByApplication = false;
          PttOnAttempted = false;
          PttReleased.Set();
          LastWrittenRxFrequency = NOT_ASSIGNED;
        }
        RequestedPtt = PendingAfterPttAck(RequestedPtt, commandPtt);
      }
      Log.Information(
        "CAT PTT command accepted: PTT={Ptt}, mode={Mode}, requestedRxHz={Rx}, requestedTxHz={Tx}, ctcssRequested={CtcssEnabled}, toneHz={Tone:F1}; command ACK is not independent RF-output verification",
        commandPtt, CatMode, RequestedRxFrequency, RequestedTxFrequency,
        CtcssEnabled, CtcssTone);
    }

    // the radio can turn the CTCSS encoder on and off
    internal bool CanEnableCtcss()
    {
      return CatMode != OperatingMode.RxOnly &&
             Caps != null &&
             commands.enable_ctcss != null &&
             commands.disable_ctcss != null &&
             Caps.Can(CatAction.enable_ctcss, false) &&
             Caps.Can(CatAction.disable_ctcss, false);
    }

    // the radio can select the CTCSS tone frequency over CAT. some rigs, e.g. the IC-706MKIIG,
    // can only switch the encoder on and off, the tone must be selected on the front panel
    internal bool CanSetCtcssTone()
    {
      return CatMode != OperatingMode.RxOnly &&
             Caps != null &&
             commands.set_ctcss_tone != null &&
             Caps.Can(CatAction.write_ctcss_tone, false);
    }

    // the arming burst needs the tone, the encoder switch and CAT PTT
    internal bool CanSendArmingTone()
    {
      return CanSetCtcssTone() && CanEnableCtcss() && CanPtt();
    }

    private bool TryWriteCtcss()
    {
      bool toneOk = true;
      bool enableOk = true;

      if (CanSetCtcssTone() && commands.set_ctcss_tone is { } toneCommand)
        toneOk = SendWriteCommand(
          toneCommand.Replace("{tone}", $"{CtcssTones.ToTenths(CtcssTone)}"));

      if (CanEnableCtcss() && (CtcssEnabled == true ? commands.enable_ctcss : commands.disable_ctcss) is { } enableCommand)
        enableOk = SendWriteCommand(enableCommand);

      bool ok = toneOk && enableOk;
      if (ok)
      {
        if (CtcssPending || CtcssReassertAfterTune)
          Log.Information(
            "CAT CTCSS write accepted: mode={Mode}, requestedToneHz={Tone:F1}, requestedEnabled={Enabled}; this is CAT ACK, not independent IC-9700 state readback",
            CatMode, CtcssTone, CtcssEnabled);
        CtcssPending = false;
      }
      else
        Log.Warning(
          "CAT CTCSS write rejected: toneOk={ToneOk}, enableOk={EnableOk}, mode={Mode}; PTT must not be armed while reassert is pending",
          toneOk, enableOk, CatMode);
      return ok;
    }

    private void TryReassertCtcssAfterTune()
    {
      if (!CtcssReassertAfterTune || !CtcssEnabled.HasValue) return;

      // Do not consume the one-shot request until all frequency/mode changes that could
      // cause a backend VFO/Main-Sub swap have actually been applied.
      if (NeedToWriteRxFrequency() || NeedToWriteTxFrequency() ||
          NeedToWriteRxMode() || NeedToWriteTxMode())
        return;

      LogInfo("Reasserting CTCSS after tune");
      if (TryWriteCtcss())
        CtcssReassertAfterTune = false;
    }

    private void TrySendArmingTone()
    {
      if (RequestedArmingTone is not double toneHz) return;
      RequestedArmingTone = null;

      if (!CanSendArmingTone() || commands.set_ctcss_tone is not { } toneCommand ||
          commands.enable_ctcss is not { } enableCommand || commands.set_ptt_on is not { } onCommand ||
          commands.set_ptt_off is not { } offCommand) return;

      // the operator is transmitting, do not interfere
      if (Ptt) return;

      Log.Information($"Sending {CtcssTones.ARMING_DURATION_MS} ms arming carrier with a {toneHz} Hz tone");

      bool toneOk = SendWriteCommand(
        toneCommand.Replace("{tone}", $"{CtcssTones.ToTenths(toneHz)}"));
      bool enableOk = SendWriteCommand(enableCommand);
      if (!toneOk || !enableOk)
      {
        Log.Warning("Arming carrier aborted because CTCSS setup was rejected.");
        return;
      }

      if (!SendWriteCommand(onCommand))
      {
        Log.Warning("Arming carrier aborted because PTT ON was rejected.");
        return;
      }

      Ptt = true;
      PttOwnedByApplication = true;
      PttReleased.Reset();
      LastWrittenTxFrequency = NOT_ASSIGNED;

      Thread.Sleep(CtcssTones.ARMING_DURATION_MS);

      if (SendWriteCommand(offCommand))
      {
        Ptt = false;
        PttOwnedByApplication = false;
        PttReleased.Set();
        LastWrittenRxFrequency = NOT_ASSIGNED;
      }
      else
      {
        // Keep our state at TX and schedule an explicit OFF retry. ReadPtt on
        // the next cycle can refine the observed hardware state.
        lock (pttRequestSync) RequestedPtt = false;
        Log.Error("Arming carrier PTT OFF was rejected; queued another PTT OFF attempt.");
      }

      // re-apply the tone and the on/off state saved for this transmitter
      _ = TryWriteCtcss();
    }
  }
}
