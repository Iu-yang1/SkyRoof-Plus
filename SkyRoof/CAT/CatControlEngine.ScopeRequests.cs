using System.Globalization;
using Serilog;
using SkyRoof.Properties;
using VE3NEA;

namespace SkyRoof
{
  public partial class CatControlEngine
  {
    // SkyCAT uses the existing CAT session for scope requests.
    private volatile bool IcomScopeOutputPending;
    private readonly IcomScopeCommandQueue IcomScopeControlCommands =
      new(capacity: 32);
    private volatile bool IcomScopeReadbackPending;
    private static readonly string[] IcomScopeReadbackFields =
    {
      "SELECT",
      "MAIN.MODE", "MAIN.SPAN", "MAIN.EDGE", "MAIN.REF",
      "MAIN.SPEED", "MAIN.VBW",
      "SUB.MODE", "SUB.SPAN", "SUB.EDGE", "SUB.REF",
      "SUB.SPEED", "SUB.VBW",
      "TX", "CENTER", "MARKER"
    };
    private readonly Dictionary<string, string> IcomScopeReadbackValues = new(
      StringComparer.OrdinalIgnoreCase);
    private int IcomScopeReadbackIndex;
    private readonly object IcomScopeReadbackSync = new();
    private int IcomScopeReadbackGeneration;
    private int LastIcomScopeReadbackFieldCount = -1;
    internal int LastIcomScopeReadbackCount =>
      Volatile.Read(ref LastIcomScopeReadbackFieldCount);

    private volatile bool IcomRfGainReadbackPending;
    private int RequestedIcomRfGain = -1;
    internal bool SupportsIcomRfGain =>
      SupportsIcomScopeOutput &&
      string.Equals(RadioCapabilities?.model, "IC-9700",
        StringComparison.OrdinalIgnoreCase);
    internal event Action<int>? IcomRfGainReadbackReceived;
    private IcomFixedEdgeReadbackRequest? IcomFixedEdgeReadbackPending;

    private sealed class IcomFixedEdgeReadbackRequest
    {
      internal int FrequencyRange { get; init; }
      internal int EdgeNumber { get; init; }
    }

    internal int PendingIcomScopeControlCount =>
      IcomScopeControlCommands.Count;

    internal long DroppedIcomScopeControlCount =>
      IcomScopeControlCommands.DroppedCount;

    private long RejectedIcomScopeControlCountValue;

    internal long RejectedIcomScopeControlCount =>
      Interlocked.Read(
        ref RejectedIcomScopeControlCountValue);

    internal event Action<IcomScopeReadbackState>?
      IcomScopeReadbackReceived;
    internal event Action<IcomFixedEdgeReadbackState>?
      IcomFixedEdgeReadbackReceived;

    internal bool SupportsIcomScopeOutput =>
      ReferenceEquals(commands, RigCtldCommands.SkyCat);

    internal bool RequestIcomRfGainReadback()
    {
      if (!SupportsIcomRfGain)
        return false;
      IcomRfGainReadbackPending = true;
      return true;
    }

    internal bool RequestIcomRfGainWrite(int value)
    {
      if (!SupportsIcomRfGain || value is < 0 or > 255)
        return false;
      Interlocked.Exchange(ref RequestedIcomRfGain, value);
      return true;
    }

    public void RequestIcomScopeOutput()
    {
      LogInfo("IC-9700 scope output reassert requested");
      IcomScopeOutputPending = true;
    }

    internal void CancelIcomScopeRequests()
    {
      IcomScopeOutputPending = false;
      lock (IcomScopeReadbackSync)
      {
        IcomScopeReadbackPending = false;
        IcomScopeReadbackIndex = 0;
        IcomScopeReadbackValues.Clear();
        IcomScopeReadbackGeneration++;
        Volatile.Write(ref LastIcomScopeReadbackFieldCount, -1);
      }
      Volatile.Write(
        ref IcomFixedEdgeReadbackPending,
        null);
      IcomScopeControlCommands.Clear();
    }

    internal bool RequestIcomScopeReadback(bool restart = false)
    {
      if (!SupportsIcomScopeOutput)
        return false;

      lock (IcomScopeReadbackSync)
      {
        // Explicit SYNC must restart an incomplete 16-register survey.
        // Previous in-flight replies are discarded by the generation ID.
        if (restart || !IcomScopeReadbackPending)
        {
          IcomScopeReadbackIndex = 0;
          IcomScopeReadbackValues.Clear();
          IcomScopeReadbackGeneration++;
          Volatile.Write(ref LastIcomScopeReadbackFieldCount, -1);
          IcomScopeReadbackPending = true;
        }
      }
      return true;
    }

    internal bool RequestIcomFixedEdgeReadback(
      int frequencyRange,
      int edgeNumber)
    {
      if (!SupportsIcomScopeOutput ||
          frequencyRange is < 1 or > 3 ||
          edgeNumber is < 1 or > 4)
        return false;

      Volatile.Write(
        ref IcomFixedEdgeReadbackPending,
        new IcomFixedEdgeReadbackRequest
        {
          FrequencyRange =
            frequencyRange,
          EdgeNumber =
            edgeNumber
        });

      return true;
    }

    internal bool RequestIcomScopeCommand(
      string command)
    {
      if (!SupportsIcomScopeOutput ||
          string.IsNullOrWhiteSpace(command) ||
          !command.StartsWith(
            "U SCOPE_",
            StringComparison.Ordinal))
        return false;

      long droppedBefore =
        IcomScopeControlCommands.DroppedCount;

      IcomScopeControlCommands.Enqueue(
        command);

      if (IcomScopeControlCommands.DroppedCount >
          droppedBefore)
        Log.Warning(
          "IC-9700 scope-control queue was full; dropped the oldest pending command before queuing {Command}",
          command);

      LogInfo(
        $"Queued IC-9700 scope control: {command}");

      return true;
    }
  }
}
