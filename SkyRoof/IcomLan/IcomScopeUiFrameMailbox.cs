namespace SkyRoof
{
  internal sealed class IcomScopeUiFrameMailbox
  {
    private readonly object gate = new();
    private readonly Queue<IcomScopeFrame>[] completed =
      [new Queue<IcomScopeFrame>(), new Queue<IcomScopeFrame>()];
    private readonly IcomScopeFrame?[] partial = new IcomScopeFrame?[2];
    private readonly int maximumCompletedSweepsPerReceiver;
    private bool callbackScheduled;
    private long replacedFrames;
    private long replacedCompleteSweeps;

    internal IcomScopeUiFrameMailbox(int maximumCompletedSweepsPerReceiver = 1)
    {
      if (maximumCompletedSweepsPerReceiver < 1 ||
          maximumCompletedSweepsPerReceiver > 64)
        throw new ArgumentOutOfRangeException(
          nameof(maximumCompletedSweepsPerReceiver));
      this.maximumCompletedSweepsPerReceiver =
        maximumCompletedSweepsPerReceiver;
    }

    internal long ReplacedFrames
    {
      get { lock (gate) return replacedFrames; }
    }

    internal long ReplacedCompleteSweeps
    {
      get { lock (gate) return replacedCompleteSweeps; }
    }

    internal bool Offer(IcomScopeFrame frame)
    {
      ArgumentNullException.ThrowIfNull(frame);
      lock (gate)
      {
        int receiver = frame.Scope == 1 ? 1 : 0;
        IcomScopeFrame? latest = partial[receiver];
        if (completed[receiver].Count > 0)
        {
          IcomScopeFrame last = completed[receiver].Last();
          if (latest == null || latest.TimestampUtc < last.TimestampUtc)
            latest = last;
        }
        if (latest != null && frame.TimestampUtc <= latest.TimestampUtc)
          return false;

        if (frame.SweepComplete)
        {
          Queue<IcomScopeFrame> queue = completed[receiver];
          if (queue.Count >= maximumCompletedSweepsPerReceiver)
          {
            queue.Dequeue();
            replacedFrames++;
            replacedCompleteSweeps++;
          }
          queue.Enqueue(frame);
          if (partial[receiver] != null)
          {
            partial[receiver] = null;
            replacedFrames++;
          }
        }
        else
        {
          if (partial[receiver] != null)
            replacedFrames++;
          partial[receiver] = frame;
        }

        if (callbackScheduled)
          return false;
        callbackScheduled = true;
        return true;
      }
    }

    internal IcomScopeFrame[] TakeAll()
    {
      lock (gate)
      {
        IcomScopeFrame[] result = completed
          .SelectMany(queue => queue)
          .Concat(partial)
          .OfType<IcomScopeFrame>()
          .OrderBy(frame => frame.TimestampUtc.Ticks)
          .ToArray();
        foreach (Queue<IcomScopeFrame> queue in completed)
          queue.Clear();
        Array.Clear(partial);
        callbackScheduled = false;
        return result;
      }
    }

    internal void Clear()
    {
      lock (gate)
      {
        foreach (Queue<IcomScopeFrame> queue in completed)
          queue.Clear();
        Array.Clear(partial);
        callbackScheduled = false;
        replacedFrames = 0;
        replacedCompleteSweeps = 0;
      }
    }
  }
}
