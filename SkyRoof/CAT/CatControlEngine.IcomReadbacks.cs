using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public partial class CatControlEngine
  {
    // One documented IC-9700 network setting is read per CAT cycle.
    // Keep Doppler, PTT and all ordinary CAT work in the existing worker.
    private static readonly string[] IcomNetworkReadbackFields =
      { "IP", "ENABLED", "CONTROL", "SERIAL", "AUDIO" };
    private readonly object IcomNetworkReadbackSync = new();
    private readonly Dictionary<string, string> IcomNetworkReadbackValues = new(
      StringComparer.Ordinal);
    private bool IcomNetworkReadbackPending;
    private int IcomNetworkReadbackIndex;
    private int IcomNetworkReadbackGeneration;
    internal event Action<IcomNetworkReadbackState>? IcomNetworkReadbackReceived;

    internal bool RequestIcomNetworkReadback()
    {
      if (!SupportsIcomRfGain)
        return false;
      lock (IcomNetworkReadbackSync)
      {
        IcomNetworkReadbackGeneration++;
        IcomNetworkReadbackIndex = 0;
        IcomNetworkReadbackValues.Clear();
        IcomNetworkReadbackPending = true;
      }
      return true;
    }

    private void TryReadIcomNetworkField()
    {
      string key;
      int generation;
      lock (IcomNetworkReadbackSync)
      {
        if (!IcomNetworkReadbackPending) return;
        key = IcomNetworkReadbackFields[IcomNetworkReadbackIndex++];
        generation = IcomNetworkReadbackGeneration;
      }

      string? reply = SendReadCommand("U NETWORK_READ_FIELD " + key);
      IcomNetworkReadbackState? completed = null;
      lock (IcomNetworkReadbackSync)
      {
        if (!IcomNetworkReadbackPending ||
            generation != IcomNetworkReadbackGeneration)
          return;
        if (reply != null &&
            reply.StartsWith(key + "=", StringComparison.Ordinal))
          IcomNetworkReadbackValues[key] = reply[(key.Length + 1)..];
        else
          Log.Warning("IC-9700 network readback {Key} unavailable: {Reply}",
            key, reply ?? "<no reply>");

        if (IcomNetworkReadbackIndex == IcomNetworkReadbackFields.Length)
        {
          IcomNetworkReadbackPending = false;
          IcomNetworkReadbackIndex = 0;
          completed = IcomNetworkReadbackState.Parse(IcomNetworkReadbackValues);
          IcomNetworkReadbackValues.Clear();
        }
      }
      if (completed != null)
        syncContext.Post(_ => IcomNetworkReadbackReceived?.Invoke(completed), null);
    }

    // Meter reads are scheduled after frequency and PTT operations, and use
    // the same SkyCAT serialized CI-V owner instead of a competing LAN session.
    private int PendingIcomMeter = -1;
    private bool MeterCommandUnsupported;
    private long MeterQueryCooldownTicks;
    private int MeterQueryFailures;
    private long LastMeterQueryTickMs;

    // A continuously tracking satellite may always have pending Doppler
    // writes. Defer background meters briefly, but do not starve them.
    internal static bool MayPollMeter(bool urgentCatWork, long sinceLastMs) =>
      !urgentCatWork || sinceLastMs >= 1200;

    internal bool MeterReadbackAvailable =>
      SupportsIcomRfGain && !MeterCommandUnsupported &&
      DateTime.UtcNow.Ticks >= Interlocked.Read(ref MeterQueryCooldownTicks);
    internal event Action<IcomMeterReading>? IcomMeterReadbackReceived;

    internal bool RequestIcomMeterReadback(IcomMeterKind kind)
    {
      if (!MeterReadbackAvailable || !Enum.IsDefined(kind))
        return false;
      Interlocked.Exchange(ref PendingIcomMeter, (int)kind);
      return true;
    }

    private void TryReadIcomMeter(bool urgentCatWork)
    {
      if (Volatile.Read(ref PendingIcomMeter) < 0) return;
      long now = Environment.TickCount64;
      if (!MayPollMeter(urgentCatWork,
            now - Interlocked.Read(ref LastMeterQueryTickMs)))
        return;
      int request = Interlocked.Exchange(ref PendingIcomMeter, -1);
      Interlocked.Exchange(ref LastMeterQueryTickMs, now);
      if (request < 0 || MeterCommandUnsupported) return;
      IcomMeterKind kind = (IcomMeterKind)request;
      string field = kind switch
      {
        IcomMeterKind.S => "S",
        IcomMeterKind.Power => "PWR",
        IcomMeterKind.Alc => "ALC",
        IcomMeterKind.Swr => "SWR",
        _ => ""
      };
      if (field.Length == 0) return;

      string? reply = SendReadCommand("U METER_READ_FIELD " + field);
      if (reply == "RPRT -11")
      {
        MeterCommandUnsupported = true;
        Log.Warning("The current SkyCAT server does not support IC-9700 meter readback. Update the pinned SkyCAT implementation.");
        return;
      }
      if (reply == null ||
          !reply.StartsWith(field + "=", StringComparison.Ordinal) ||
          !int.TryParse(reply.AsSpan(field.Length + 1),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out int raw) ||
          raw is < 0 or > 255)
      {
        if (++MeterQueryFailures >= 2)
        {
          Interlocked.Exchange(ref MeterQueryCooldownTicks,
            DateTime.UtcNow.AddSeconds(15).Ticks);
          MeterQueryFailures = 0;
          Log.Warning(
            "SkyCAT IC-9700 meter reads unavailable ({Reply}); pausing CAT queries for 15s so Doppler/PTT remain responsive. Direct LAN polling may continue.",
            reply ?? "<no response>");
        }
        return;
      }

      MeterQueryFailures = 0;
      var reading = new IcomMeterReading(kind, raw);
      syncContext.Post(_ => IcomMeterReadbackReceived?.Invoke(reading), null);
    }
  }
}
