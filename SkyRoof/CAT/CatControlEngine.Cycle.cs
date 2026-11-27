using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public partial class CatControlEngine
  {
    protected override void Cycle()
    {
      ReadPtt();
      bool urgentCatWork = GetRequestedPtt().HasValue ||
        NeedToWriteRxFrequency() || NeedToWriteTxFrequency() ||
        NeedToWriteRxMode() || NeedToWriteTxMode();
      // A new ON arriving after this snapshot must wait for the next cycle.
      // Otherwise StageTxForPttOn can return before a concurrently queued ON.
      bool txPrepared = GetRequestedPtt() == true && StageTxForPttOn();

      // If a requested CTCSS update failed, do not key the transmitter with a
      // stale/unknown encoder state. Keep the write pending and retry next cycle.
      bool ctcssReady = !NeedToWriteCtcss() || TryWriteCtcss();
      // CTCSS readiness may block KEYING, but must NEVER block emergency
      // UNKEY. A failed tone write while SSTV is transmitting cannot keep
      // PTT ON merely because the encoder remains in an unknown state.
      TryWritePtt(ctcssReady, txPrepared);

      if (NeedToReadRxFrequency()) TryReadRxFrequency();
      if (NeedToReadTxFrequency()) TryReadTxFrequency();

      if (NeedToWriteRxFrequency()) TryWriteRxFrequency(RequestedRxFrequency);
      if (NeedToWriteTxFrequency()) TryWriteTxFrequency();

      if (NeedToWriteRxMode()) TryWriteRxMode();
      if (NeedToWriteTxMode()) TryWriteTxMode();

      TryReassertCtcssAfterTune();

      if (IcomScopeOutputPending) TryEnableIcomScopeOutput();

      if (IcomScopeControlCommands.TryDequeue(
            out string? scopeCommand))
        TryWriteIcomScopeControl(
          scopeCommand);

      // Latest slider value wins; all radio I/O runs on this CAT thread.
      int rfGainToWrite = Interlocked.Exchange(
        ref RequestedIcomRfGain, -1);
      if (rfGainToWrite >= 0)
        TryWriteIcomRfGain(rfGainToWrite);

      if (IcomRfGainReadbackPending)
        TryReadIcomRfGain();

      TryReadIcomNetworkField();
      TryReadIcomMeter(urgentCatWork);

      if (IcomScopeReadbackPending)
        TryReadIcomScopeState();

      IcomFixedEdgeReadbackRequest? fixedEdgeRequest =
        Interlocked.Exchange(
          ref IcomFixedEdgeReadbackPending,
          null);

      if (fixedEdgeRequest != null)
        TryReadIcomFixedEdge(
          fixedEdgeRequest);

      if (RequestedArmingTone.HasValue) TrySendArmingTone();
    }

    protected override bool Setup()
    {
      try
      {
        SelectOperatingMode();

        switch (CatMode)
        {
          case OperatingMode.RxOnly:
          case OperatingMode.TxOnly:
          case OperatingMode.Simplex: return SendWriteCommands(commands.setup_simplex); 
          case OperatingMode.Split:   return SendWriteCommands(commands.setup_split); 
          case OperatingMode.Duplex:  return SendWriteCommands(commands.setup_duplex); 
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to set up radio.");
      }

      return false;
      }

    private RigCtldCommands commands = RigCtldCommands.RigCtld;

    private void SelectOperatingMode()
    {
        // get radio capabilities, either from SkyCAT for from a file
        RadioCapabilities = ReadCapabilitiesFromSkyCat() ?? RadioCapabilities.LoadDefaultCapabilities();
        Log.Information($"Loaded radio capabilities for: {RadioCapabilities.model}");

        // Set appropriate command set
        commands = RadioCapabilities.model == "rigctld.exe" ?
            RigCtldCommands.RigCtld : RigCtldCommands.SkyCat;

        // determmine CatMode, get radio Caps in that mode
        if (rx && !tx)
        {
            CatMode = OperatingMode.RxOnly;
            Caps = RadioCapabilities.simplex ??
              throw new InvalidDataException("CAT capabilities missing required simplex mode");
        }
        else if (!rx && tx)
        {
            CatMode = OperatingMode.TxOnly;
            Caps = RadioCapabilities.simplex ??
              throw new InvalidDataException("CAT capabilities missing required simplex mode");
        }
        else if (crossband && RadioCapabilities.duplex != null)
        {
            CatMode = OperatingMode.Duplex;
            Caps = RadioCapabilities.duplex ??
                throw new InvalidDataException("CAT capabilities missing duplex mode");
        }
        else if (RadioCapabilities.CanSplitTune(crossband))
        {
            CatMode = OperatingMode.Split;
            Caps = RadioCapabilities.split ??
                throw new InvalidDataException("CAT capabilities missing split mode");
        }
        else if (RadioCapabilities.simplex != null)
        {
            CatMode = OperatingMode.Simplex;
            Caps = RadioCapabilities.simplex ??
              throw new InvalidDataException("CAT capabilities missing required simplex mode");
        }
        else
            throw new Exception("Radio does not support any operating modes");
    }

    private RadioCapabilities? ReadCapabilitiesFromSkyCat()
    {
      string json = SendReadCommand("a") ?? string.Empty;

      // The private "a" capability command exists only in SkyCAT. A generic
      // rigctld may answer with different RPRT error codes depending on Hamlib
      // version/backend; any RPRT response means "no SkyCAT capability JSON" and
      // should fall back to the embedded rigctld capability description.
      if (json.StartsWith("RPRT ", StringComparison.Ordinal))
        return null;

      if (json == string.Empty) 
        throw new Exception("Failed to read radio capabilities from SkyCAT");

      return RadioCapabilities.LoadFromJson(json) ??
        throw new Exception("Failed to parse radio capabilities from SkyCAT");
    }
  }
}
