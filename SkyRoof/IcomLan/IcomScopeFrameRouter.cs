namespace SkyRoof
{
  // Serializes selection/session boundaries with the two independent
  // capture producers. Only the cheap ordered ingress is under this lock.
  internal sealed class IcomScopeFrameRouter
  {
    private readonly object gate = new();
    private readonly long[] lastTicks = new long[2];
    private int epoch;
    private bool active, held;
    internal IcomScopeState State { get; } = new();

    internal void Start(int session, Action clear)
    {
      lock (gate)
      {
        epoch = session;
        active = true;
        held = false;
        Array.Clear(lastTicks);
        State.Clear();
        clear();
      }
    }

    internal void Stop(int session, Action clear)
    {
      lock (gate)
      {
        epoch = session;
        active = held = false;
        Array.Clear(lastTicks);
        State.Clear();
        clear();
      }
    }

    internal void Clear(Action clear)
    {
      lock (gate)
      {
        State.Clear();
        clear();
      }
    }

    internal void SetHold(bool value)
    {
      lock (gate) held = value;
    }

    internal void Select(IcomLanScopeBand band, Action clear, Action<IcomScopeFrame> waveform)
    {
      lock (gate)
      {
        if (State.SelectedBand == band) return;
        if (band != IcomLanScopeBand.Auto) State.Clear();
        State.SelectedBand = band;
        if (held) return;
        clear();
        if (State.LatestSelectedFrame is { } cached) waveform(cached);
      }
    }

    // Metadata can be coalesced by the UI independently of complete sweeps.
    // Repeated fallback reads and callbacks from a retired capture are rejected.
    internal bool Offer(int session, IcomScopeFrame frame, Action<IcomScopeFrame> waveform,
      Func<IcomScopeFrame, bool> metadata)
    {
      lock (gate)
      {
        if (!active || epoch != session) return false;
        int scope = frame.Scope == 1 ? 1 : 0;
        long ticks = frame.TimestampUtc.Ticks;
        if (ticks <= lastTicks[scope]) return false;
        lastTicks[scope] = ticks;
        if (held) return false;
        State.Update(frame);
        if (State.ShouldDisplay(frame)) waveform(frame);
        return metadata(frame);
      }
    }
  }
}
