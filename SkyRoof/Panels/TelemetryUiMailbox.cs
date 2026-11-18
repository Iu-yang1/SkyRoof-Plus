using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SkyRoof
{
  /// <summary>
  /// Non-blocking, ordered WinForms event mailbox. Decoded frames and image
  /// events are never dropped; batches yield to the window message pump after
  /// at most 24 callbacks or 5 ms so waterfall paint and user input can run.
  /// The delegate schedules one UI-thread invocation without waiting for it.
  /// </summary>
  internal sealed class TelemetryUiMailbox
  {
    private readonly ConcurrentQueue<Action> messages = new();
    private readonly Action<Action> schedule;
    private int scheduled;
    private int closed;
    internal int PendingCount => messages.Count;

    internal TelemetryUiMailbox(Action<Action> schedule) =>
      this.schedule = schedule;

    internal void Post(Action callback)
    {
      if (Volatile.Read(ref closed) != 0) return;
      messages.Enqueue(callback);
      if (Interlocked.CompareExchange(ref scheduled, 1, 0) == 0)
        schedule(Drain);
    }

    internal void Drain()
    {
      var watch = Stopwatch.StartNew();
      int processed = 0;
      while (processed < 24 && watch.ElapsedMilliseconds < 5 &&
        messages.TryDequeue(out var callback))
      {
        if (Volatile.Read(ref closed) != 0) break;
        try { callback(); }
        catch (Exception e) { Log.Error(e, "Telemetry UI event failed"); }
        processed++;
      }

      if (Volatile.Read(ref closed) != 0)
      {
        while (messages.TryDequeue(out _)) { }
        Interlocked.Exchange(ref scheduled, 0);
        return;
      }

      if (!messages.IsEmpty)
      {
        schedule(Drain);
        return;
      }
      Interlocked.Exchange(ref scheduled, 0);
      if (!messages.IsEmpty &&
        Interlocked.CompareExchange(ref scheduled, 1, 0) == 0)
        schedule(Drain);
    }

    internal void Close()
    {
      Interlocked.Exchange(ref closed, 1);
      while (messages.TryDequeue(out _)) { }
    }
  }

  /// <summary>Only the newest in-progress UI value is useful; completed
  /// frame/image events still go through the lossless ordered mailbox.</summary>
  internal sealed class TelemetryLatestUiValue<T> where T : class
  {
    private readonly TelemetryUiMailbox mailbox;
    private readonly Action<T> updateUi;
    private T? latest;
    private int pending;

    internal TelemetryLatestUiValue(TelemetryUiMailbox mailbox, Action<T> updateUi)
    {
      this.mailbox = mailbox;
      this.updateUi = updateUi;
    }

    internal void Offer(T value)
    {
      Interlocked.Exchange(ref latest, value);
      if (Interlocked.CompareExchange(ref pending, 1, 0) == 0)
        mailbox.Post(Drain);
    }

    private void Drain()
    {
      var value = Interlocked.Exchange(ref latest, null);
      if (value != null) updateUi(value);
      Interlocked.Exchange(ref pending, 0);
      if (Volatile.Read(ref latest) != null &&
          Interlocked.CompareExchange(ref pending, 1, 0) == 0)
        mailbox.Post(Drain);
    }
  }

  /// <summary>
  /// Serializes decoder worker joins and native FFTW/decoder teardown OUTSIDE
  /// WinForms. Keeping cleanup ordered also lets the last retired decoder
  /// finish using the shared FM speech model before the model is released.
  /// </summary>
  internal sealed class TelemetryDecoderRetirement
  {
    private readonly object gate = new();
    private Task pending = Task.CompletedTask;

    internal Task Completion { get { lock (gate) return pending; } }

    internal void Retire(TelemetryDecocder decoder)
    {
      decoder.StopAcceptingIq();
      Enqueue(() =>
      {
        decoder.Purge();
        decoder.Dispose();
      });
    }

    internal void Enqueue(Action cleanup)
    {
      lock (gate)
        pending = pending.ContinueWith(_ =>
        {
          try { cleanup(); }
          catch (Exception ex) { Log.Error(ex, "Telemetry decoder retirement failed"); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
  }
}
