using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public partial class CatControlEngine
  {
    private void TryReadRxFrequency()
    {
      string command = GetReadRxFrequencyCommand();
      if (command == string.Empty) return;

      long? readFrequency = ReadFrequency(command);
      if (!readFrequency.HasValue) return;
      long frequency = readFrequency.Value;

      DialKnobSpinning = !IgnoreDialKnob &&
        LastReadRxFrequency != 0 && // first read - ignore, no previous value
        IsDiff(frequency, LastReadRxFrequency) &&    // same freq as before, no change
        IsDiff(frequency, LastWrittenRxFrequency) && // new freq was written, ingnore
          IsDiff(frequency, LastWrittenTxFrequency); // tx freq read by accident, ignore

      LastReadRxFrequency = frequency;
      RxReadbackSampled?.Invoke(frequency, DateTime.UtcNow);
      
      if (DialKnobSpinning)
      {
        // cancel previously requested frequency changes, they would undo the dial knob change
        if (RequestedRxFrequency != NOT_ASSIGNED && IsDiff(RequestedRxFrequency, frequency))
          LogInfo($"Canceling pending RX frequency change ({RequestedRxFrequency}) due to manual change ({frequency})");
        RequestedRxFrequency = NOT_ASSIGNED;
        RequestedTxFrequency = NOT_ASSIGNED;

        OnRxFrequencyChanged();
      }
    }

    private void TryReadTxFrequency()
    {
      string command = GetReadTxFrequencyCommand();
      if (command == string.Empty) return;

      long? readFrequency = ReadFrequency(command);
      if (!readFrequency.HasValue) return;
      long frequency = readFrequency.Value;

      bool changed = LastReadTxFrequency != 0 && LastWrittenTxFrequency != 0 &&
        IsDiff(frequency, LastReadTxFrequency) && IsDiff(frequency, LastWrittenTxFrequency) &&
        IsDiff(frequency, LastWrittenRxFrequency);

      LastReadTxFrequency = frequency;
      if (changed) OnTxFrequencyChanged();
    }

    private long? ReadFrequency(string command)
    {
      var reply = SendReadCommand(command);
      if (reply == null) return null;

      if (!long.TryParse(reply, CultureInfo.InvariantCulture, out long frequency))
      {
        BadReply(reply);
        return null;
      }

      return RoundToStep(frequency);
    }

    private bool TryWriteRxFrequency(long frequency)
    {
      if (frequency == NOT_ASSIGNED) return false;
      string command = GetWriteRxFrequencyCommand(frequency);
      if (command == string.Empty || !SendWriteCommand(command)) return false;
      LastWrittenRxFrequency = frequency;
      LogFreqs("Rx frequency written");
      return true;
    }

    private bool TryWriteTxFrequency()
    {
      long frequency = RequestedTxFrequency;
      string command = GetWriteTxFrequencyCommand(frequency);
      if (command == string.Empty || !SendWriteCommand(command)) return false;
      LastWrittenTxFrequency = frequency;
      LogFreqs("Tx frequency written");
      return true;
    }

    private bool StageTxForPttOn()
    {
      if (Ptt || CatMode == OperatingMode.RxOnly)
        return true;

      if (CatMode == OperatingMode.Simplex)
      {
        // One-VFO radios tune the RX dial to TX before asserting PTT.
        if (RequestedTxFrequency != NOT_ASSIGNED &&
            GetWriteRxFrequencyCommand(RequestedTxFrequency) != string.Empty &&
            !TryWriteRxFrequency(RequestedTxFrequency))
          return false;

        if (RequestedTxMode.HasValue &&
            GetWriteRxModeCommand(RequestedTxMode.Value) != string.Empty &&
            !TryWriteRxMode(RequestedTxMode))
          return false;

        return true;
      }

      // A separate TX VFO / TX-only CAT endpoint must have accepted the
      // pending transmit frequency and mode before keying. If a command is
      // not advertised, the operator may continue to manage that field.
      if (NeedToWriteTxFrequency() &&
          GetWriteTxFrequencyCommand(RequestedTxFrequency) != string.Empty &&
          !TryWriteTxFrequency())
        return false;

      if (RequestedTxMode.HasValue && NeedToWriteTxMode() &&
          GetWriteTxModeCommand(RequestedTxMode.Value) != string.Empty &&
          !TryWriteTxMode())
        return false;

      return true;
    }

    private bool TryWriteRxMode(Slicer.Mode? mode = null)
    {
      if ((mode ?? RequestedRxMode) is not Slicer.Mode newMode)
      {
        Log.Warning("CAT RX mode write skipped: no requested mode");
        return false;
      }

      string command = GetWriteRxModeCommand(newMode);
      if (command == string.Empty) return false;

      if (!SendWriteCommand(command))
      {
        RemoveDataFromMode(ref newMode);
        command = GetWriteRxModeCommand(newMode);
        if (command == string.Empty || !SendWriteCommand(command))
          return false;
      }

      LastWrittenRxMode = RequestedRxMode;
      return true;
    }

    private bool TryWriteTxMode()
    {
      if (RequestedTxMode is not Slicer.Mode mode)
      {
        Log.Warning("CAT TX mode write skipped: no requested mode");
        return false;
      }

      string command = GetWriteTxModeCommand(mode);
      if (command == string.Empty) return false;

      if (!SendWriteCommand(command))
      {
        RemoveDataFromMode(ref mode);
        command = GetWriteTxModeCommand(mode);
        if (command == string.Empty || !SendWriteCommand(command))
          return false;
      }

      LastWrittenTxMode = mode;
      return true;
    }

    private void RemoveDataFromMode(ref Slicer.Mode mode)
    {
      mode = mode switch
      {
        Slicer.Mode.USB_D => Slicer.Mode.USB,
        Slicer.Mode.LSB_D => Slicer.Mode.LSB,
        Slicer.Mode.FM_D => Slicer.Mode.FM,
        _ => mode
      };
    }
  }
}
