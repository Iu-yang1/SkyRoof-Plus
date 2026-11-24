using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public partial class CatControlEngine
  {
    private string GetReadRxFrequencyCommand()
    {
      switch (CatMode)
      {
        case OperatingMode.TxOnly:
          return string.Empty;

        case OperatingMode.RxOnly:
          if (Ptt == false && Caps.Can(CatAction.read_rx_frequency, Ptt)) return commands.read_rx_frequency ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.read_tx_frequency, Ptt)) return commands.read_tx_frequency ?? string.Empty;
          break;

        case OperatingMode.Simplex:
          if (Ptt == false && Caps.Can(CatAction.read_rx_frequency, Ptt)) return commands.read_rx_frequency ?? string.Empty;
          break;

        // split or duplex
        default:
          if (Caps.Can(CatAction.read_rx_frequency, Ptt)) return commands.read_rx_frequency ?? string.Empty;
          break;
      }

      return string.Empty;
    }

    private string GetReadTxFrequencyCommand()
    {
      switch (CatMode)
      {
        case OperatingMode.RxOnly:
          return string.Empty;

        case OperatingMode.TxOnly:
          if (Ptt == false && Caps.Can(CatAction.read_rx_frequency, Ptt)) return commands.read_rx_frequency ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.read_tx_frequency, Ptt)) return commands.read_tx_frequency ?? string.Empty;
          break;

        case OperatingMode.Simplex:
          if (Ptt == true && Caps.Can(CatAction.read_tx_frequency, Ptt)) return commands.read_tx_frequency ?? string.Empty;
          break;

        // split or duplex
        default:
          if (Caps.Can(CatAction.read_tx_frequency, Ptt)) return commands.read_tx_frequency ?? string.Empty;
          break;
      }

      return string.Empty;
    }

    private string GetWriteRxFrequencyCommand(long frequency)
    {
      switch (CatMode)
      {
        case OperatingMode.TxOnly:
          return string.Empty;

        case OperatingMode.RxOnly:
          if (Ptt == false && Caps.Can(CatAction.write_rx_frequency, Ptt)) return commands.write_rx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          else if (Ptt == true && Caps.Can(CatAction.write_tx_frequency, Ptt)) return commands.write_tx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          break;

        case OperatingMode.Simplex:
          if (Ptt == false && Caps.Can(CatAction.write_rx_frequency, Ptt)) return commands.write_rx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          break;

        default:
          if (Caps.Can(CatAction.write_rx_frequency, Ptt)) return commands.write_rx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          break;
      }

      return string.Empty;
    }

    private string GetWriteTxFrequencyCommand(long frequency)
    {
      switch (CatMode)
      {
        case OperatingMode.RxOnly:
          return string.Empty;

        case OperatingMode.TxOnly:
          if (Ptt == false && Caps.Can(CatAction.write_rx_frequency, Ptt)) return commands.write_rx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.write_tx_frequency, Ptt)) return commands.write_tx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          break;

        case OperatingMode.Simplex:
          if (Ptt == true && Caps.Can(CatAction.write_tx_frequency, Ptt)) return commands.write_tx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          break;

        default:
          if (Caps.Can(CatAction.write_tx_frequency, Ptt)) return commands.write_tx_frequency?.Replace("{frequency}", $"{frequency}") ?? string.Empty;
          break;
      }

      return string.Empty;
    }

    private string GetWriteRxModeCommand(Slicer.Mode mode)
    {
      switch (CatMode)
      {
        case OperatingMode.TxOnly:
          return string.Empty;

        case OperatingMode.RxOnly:
          if (Ptt == false && Caps.CanSetup(CatAction.write_rx_mode)) return commands.write_rx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.write_tx_mode, Ptt)) return commands.write_tx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          break;

        case OperatingMode.Simplex:
          if (Ptt == false && Caps.CanSetup(CatAction.write_rx_mode)) return commands.write_rx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          break;

        default:
          if (Ptt == false && Caps.CanSetup(CatAction.write_rx_mode)) return commands.write_rx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.write_rx_mode, Ptt)) return commands.write_rx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          break;
      }

      return string.Empty;
    }

    private string GetWriteTxModeCommand(Slicer.Mode mode)
    {
      switch (CatMode)
      {
        case OperatingMode.RxOnly:
          return string.Empty;

        case OperatingMode.TxOnly:
          if (Ptt == false && Caps.CanSetup(CatAction.write_rx_mode)) return commands.write_rx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.write_tx_mode, Ptt)) return commands.write_tx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          break;

        case OperatingMode.Simplex:
          if (Ptt == false && Caps.CanSetup(CatAction.write_tx_mode)) return commands.write_tx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.write_tx_mode, Ptt)) return commands.write_tx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          break;

        default:
          if (Ptt == false && Caps.CanSetup(CatAction.write_tx_mode)) return commands.write_tx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          if (Ptt == true && Caps.Can(CatAction.write_tx_mode, Ptt)) return commands.write_tx_mode?.Replace("{mode}", $"{mode}") ?? string.Empty;
          break;
      }

      return string.Empty;
    }

    private bool NeedToWriteTxFreqModeBeforePtt()
    {
      // not simplex
      if (CatMode != OperatingMode.Simplex) return false;

      // not swsitching to tx
      if (GetRequestedPtt() != true) return false;

      // nothing to write
      if (RequestedTxFrequency == NOT_ASSIGNED && !RequestedTxMode.HasValue) return false;

      return true;
    }

    private bool NeedToReadRxFrequency()
    {
      if (CatMode == OperatingMode.TxOnly) return false;
      return ShouldReadRxFrequency(IgnoreDialKnob,
        AfcReadbackRequired?.Invoke() == true,
        Ptt == false || CatMode == OperatingMode.RxOnly);
    }

    private bool NeedToReadTxFrequency()
    {
      if (CatMode == OperatingMode.RxOnly) return false;
      return !IgnoreDialKnob && (Ptt == true || CatMode == OperatingMode.TxOnly);
    }

    private bool NeedToWriteRxFrequency()
    {
      if (CatMode == OperatingMode.TxOnly) return false;
      if (RequestedRxFrequency == NOT_ASSIGNED) return false; // never assigned
      if (CatMode == OperatingMode.Simplex && PttChanged) return true;
      return IsDiff(RequestedRxFrequency, LastWrittenRxFrequency);
    }

    private bool NeedToWriteTxFrequency()
    {
      if (CatMode == OperatingMode.RxOnly) return false;
      if (RequestedTxFrequency == NOT_ASSIGNED) return false;
      if (CatMode == OperatingMode.Simplex && PttChanged) return true;
      return IsDiff(RequestedTxFrequency, LastWrittenTxFrequency);
    }

    private bool NeedToWriteCtcss()
    {
      // never set by the app
      if (!CtcssEnabled.HasValue) return false;
      if (CtcssPending) return true;

      // Icom Auto Repeater and similar features may drop the tone when the frequency changes,
      // re-assert it immediately before every transmission
      return GetRequestedPtt() == true && Ptt == false;
    }

    private bool NeedToWriteRxMode()
    {
      if (CatMode == OperatingMode.TxOnly) return false;
      if (!RequestedRxMode.HasValue) return false;
      if (CatMode == OperatingMode.Simplex && PttChanged) return true;
      return RequestedRxMode != LastWrittenRxMode;
    }

    private bool NeedToWriteTxMode()
    {
      if (CatMode == OperatingMode.RxOnly) return false;
      if (!RequestedTxMode.HasValue) return false;
      if (CatMode == OperatingMode.Simplex && PttChanged) return true;
      return RequestedTxMode != LastWrittenTxMode;
    }

    // may be changed to allow some slack
    private bool IsDiff(long freq1, long freq2)
    {
      return Math.Abs(freq1 - freq2) > 0;
    }
  }
}
