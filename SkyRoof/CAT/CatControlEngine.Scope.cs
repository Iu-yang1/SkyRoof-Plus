using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public partial class CatControlEngine
  {
    private void TryEnableIcomScopeOutput()
    {
      IcomScopeOutputPending = false;

      // These are SkyCAT extensions backed by IC-9700 CI-V 27 10 / 27 11.
      // Generic rigctld does not expose an equivalent command, so simply leave passive
      // sniffing in place when SkyCAT is not the active backend.
      if (!ReferenceEquals(commands, RigCtldCommands.SkyCat))
      {
        LogInfo("IC-9700 scope output request skipped: CAT backend is not SkyCAT");
        return;
      }

      bool scopeOk = SendWriteCommand("U SCOPE 1");
      bool dataOk = SendWriteCommand("U SCOPE_DATA 1");

      if (scopeOk && dataOk)
        LogInfo(
          "IC-9700 scope and waveform output enabled through SkyCAT");
      else
        Log.Warning(
          "SkyCAT did not accept all IC-9700 scope output commands. " +
          "Update SkyCAT to a build that supports U SCOPE / U SCOPE_DATA.");
    }

    private void TryReadIcomFixedEdge(
      IcomFixedEdgeReadbackRequest request)
    {
      if (!ReferenceEquals(
            commands,
            RigCtldCommands.SkyCat))
        return;

      string? reply =
        SendReadCommand(
          $"U SCOPE_READ_EDGE {request.FrequencyRange} {request.EdgeNumber}");

      if (string.IsNullOrWhiteSpace(
            reply) ||
          reply.StartsWith(
            "RPRT ",
            StringComparison.Ordinal))
      {
        Log.Warning(
          "SkyCAT IC-9700 fixed-edge readback failed for range {Range}, edge {Edge}: {Reply}",
          request.FrequencyRange,
          request.EdgeNumber,
          reply ?? "<null>");
        return;
      }

      try
      {
        IcomFixedEdgeReadbackState state =
          IcomFixedEdgeReadbackState.Parse(
            reply);

        IcomFixedEdgeReadbackReceived?.Invoke(
          state);
      }
      catch (FormatException ex)
      {
        Log.Warning(
          ex,
          "SkyCAT returned malformed fixed-edge readback: {Reply}",
          reply);
      }
    }

    private void TryWriteIcomRfGain(int gain)
    {
      if (!SupportsIcomRfGain)
        return;

      if (!SendWriteCommand($"U RF_GAIN {gain}"))
      {
        Log.Warning("SkyCAT rejected IC-9700 RF gain: {Gain}", gain);
        return;
      }

      // Confirm the actual register value, do not assume command acceptance
      // implies the physical RF gain knob changed.
      IcomRfGainReadbackPending = true;
    }

    private void TryReadIcomRfGain()
    {
      IcomRfGainReadbackPending = false;
      if (!SupportsIcomRfGain)
        return;

      string? reply = SendReadCommand("U RF_GAIN_READ");
      if (reply == null || reply.StartsWith("RPRT ", StringComparison.Ordinal) ||
          !int.TryParse(reply, NumberStyles.None,
            CultureInfo.InvariantCulture, out int gain) ||
          gain is < 0 or > 255)
      {
        Log.Warning("SkyCAT RF gain readback failed: {Reply}", reply);
        return;
      }

      syncContext.Post(_ => IcomRfGainReadbackReceived?.Invoke(gain), null);
    }

    private void TryReadIcomScopeState()
    {
      // A single radio register is queried on this CAT cycle. Do NOT hold
      // the state lock during socket I/O: UI cancellation stays responsive.
      // A generation marker discards late responses after Cancel or a write.
      if (!ReferenceEquals(commands, RigCtldCommands.SkyCat))
      {
        lock (IcomScopeReadbackSync)
        {
          IcomScopeReadbackPending = false;
          IcomScopeReadbackIndex = 0;
          IcomScopeReadbackValues.Clear();
          IcomScopeReadbackGeneration++;
        }
        return;
      }

      string key;
      int generation;
      lock (IcomScopeReadbackSync)
      {
        if (!IcomScopeReadbackPending ||
            IcomScopeReadbackIndex >= IcomScopeReadbackFields.Length)
          return;
        key = IcomScopeReadbackFields[IcomScopeReadbackIndex++];
        generation = IcomScopeReadbackGeneration;
      }

      string? reply = SendReadCommand($"U SCOPE_READ_FIELD {key}");
      string? acceptedValue = ValidateIcomScopeFieldValue(key, reply);
      string? partial = null;
      int successful = 0;
      lock (IcomScopeReadbackSync)
      {
        if (!IcomScopeReadbackPending ||
            generation != IcomScopeReadbackGeneration)
          return;

        if (acceptedValue != null)
          IcomScopeReadbackValues[key] = acceptedValue;
        else
          Log.Warning(
            "SkyCAT scope field {Field} unavailable: {Reply}; continuing other fields.",
            key, reply ?? "<no reply>");

        if (IcomScopeReadbackIndex != IcomScopeReadbackFields.Length)
          return;

        IcomScopeReadbackPending = false;
        IcomScopeReadbackIndex = 0;
        successful = IcomScopeReadbackValues.Count;
        Volatile.Write(ref LastIcomScopeReadbackFieldCount, successful);
        if (successful > 0)
          partial = string.Join(";",
            IcomScopeReadbackValues.Select(kv => $"{kv.Key}={kv.Value}"));
        IcomScopeReadbackValues.Clear();
      }

      if (partial == null)
      {
        Log.Warning(
          "IC-9700 scope synchronization received zero of {Total} fields. " +
          "The 27 xx query path is unresponsive although the waveform stream may remain live.",
          IcomScopeReadbackFields.Length);
        return;
      }
      try
      {
        IcomScopeReadbackState state = IcomScopeReadbackState.ParsePartial(partial);
        IcomScopeReadbackReceived?.Invoke(state);
        if (state.IsPartial)
          Log.Warning(
            "IC-9700 scope synchronization partial ({Received}/{Total}); missing properties are preserved.",
            successful, IcomScopeReadbackFields.Length);
      }
      catch (FormatException ex)
      {
        Log.Warning(ex, "Malformed partial scope readback: {Readback}", partial);
      }
    }

    private string? ValidateIcomScopeFieldValue(string key, string? reply)
    {
      if (reply == null ||
          !reply.StartsWith(key + "=", StringComparison.Ordinal))
        return null;

      try
      {
        // Reject only the malformed field, not valid results collected
        // from other radio registers in this synchronization survey.
        _ = IcomScopeReadbackState.ParsePartial(reply);
        return reply[(key.Length + 1)..];
      }
      catch (FormatException ex)
      {
        Log.Warning(ex, "Malformed SkyCAT scope field {Field}: {Reply}", key, reply);
        return null;
      }
    }

    private void TryWriteIcomScopeControl(
      string command)
    {
      // An operator change invalidates values collected earlier in the batch;
      // restart the next read at SELECT to avoid mixing before/after states.
      lock (IcomScopeReadbackSync)
      {
        if (IcomScopeReadbackPending)
        {
          IcomScopeReadbackIndex = 0;
          IcomScopeReadbackValues.Clear();
          IcomScopeReadbackGeneration++;
        }
      }
      if (!ReferenceEquals(
            commands,
            RigCtldCommands.SkyCat))
      {
        LogInfo(
          $"IC-9700 scope control skipped because CAT backend is not SkyCAT: {command}");
        return;
      }

      if (!SendWriteCommand(command))
      {
        Interlocked.Increment(
          ref RejectedIcomScopeControlCountValue);

        Log.Warning(
          "SkyCAT rejected IC-9700 scope control command: {Command}",
          command);
      }
    }
  }
}
