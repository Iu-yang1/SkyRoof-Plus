namespace SkyRoof
{
  // Serial command and frame processing, with no WinForms calls or GDI objects.
  internal sealed class IcomScopeRenderWorker : IDisposable
  {
    private const int MaximumQueuedFrames = 256;
    private sealed record Work(IcomScopeFrame? Frame, Action<IcomScopeRenderModel>? Command);
    private readonly object gate = new();
    private readonly LinkedList<Work> pending = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Action published;
    private readonly Thread thread;
    private IcomScopeRenderSnapshot? latest;
    private bool stopped;
    private int frameCount;
    private long droppedSweeps, droppedPartials, processedSweeps;

    internal IcomScopeRenderWorker(Action published)
    {
      this.published = published;
      thread = new Thread(Run) { IsBackground = true, Name = "Icom Scope Processing" };
      thread.Start();
    }

    internal IcomScopeRenderSnapshot? Latest => Volatile.Read(ref latest);
    internal long ProcessedSweeps => Interlocked.Read(ref processedSweeps);
    internal long DroppedSweeps => Interlocked.Read(ref droppedSweeps);
    internal long DroppedPartials => Interlocked.Read(ref droppedPartials);

    internal void Offer(IcomScopeFrame frame)
    {
      lock (gate)
      {
        if (stopped) return;
        if (frameCount == MaximumQueuedFrames) MakeRoom();
        pending.AddLast(new Work(frame, null));
        frameCount++;
        wake.Set();
      }
    }

    // Preserve configuration commands and prefer dropping partials only
    // when the bounded queue is exhausted. Count all dropped complete rows.
    private void MakeRoom()
    {
      LinkedListNode<Work>? firstSweep = null;
      for (var node = pending.First; node != null; node = node.Next)
      {
        if (node.Value.Frame is not { } frame) continue;
        if (!frame.SweepComplete)
        {
          pending.Remove(node);
          frameCount--;
          Interlocked.Increment(ref droppedPartials);
          return;
        }
        firstSweep ??= node;
      }
      if (firstSweep != null)
      {
        pending.Remove(firstSweep);
        frameCount--;
        Interlocked.Increment(ref droppedSweeps);
      }
    }

    internal void Configure(Action<IcomScopeRenderModel> command)
    {
      lock (gate)
      {
        if (stopped) return;
        pending.AddLast(new Work(null, command));
        wake.Set();
      }
    }

    private void Run()
    {
      var model = new IcomScopeRenderModel();
      try
      {
        Publish(model);
        while (true)
        {
          wake.WaitOne();
          Work[] batch;
          lock (gate)
          {
            if (stopped) return;
            batch = pending.ToArray();
            pending.Clear();
            frameCount = 0;
          }
          if (batch.Length == 0) continue;
          foreach (var item in batch)
          {
            if (item.Frame is { } frame)
            {
              model.Push(frame);
              if (frame.SweepComplete) Interlocked.Increment(ref processedSweeps);
            }
            else item.Command?.Invoke(model);
          }
          Publish(model);
        }
      }
      catch (Exception ex)
      {
        Serilog.Log.Error(ex, "Icom scope render worker failed");
      }
      finally
      {
        lock (gate)
        {
          stopped = true;
          pending.Clear();
          wake.Dispose();
        }
      }
    }

    private void Publish(IcomScopeRenderModel model)
    {
      Volatile.Write(ref latest, model.Snapshot());
      published();
    }

    public void Dispose()
    {
      lock (gate)
      {
        if (stopped) return;
        stopped = true;
        wake.Set();
      }
    }
  }
}
