using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class TelemetryUiLivenessTests
{
  [Fact]
  public void BurstAndFrameCallbacksAreOrderedButYieldEvery24Operations()
  {
    var scheduled = new ConcurrentQueue<Action>();
    var received = new List<int>();
    var mailbox = new TelemetryUiMailbox(scheduled.Enqueue);
    for (int i = 0; i < 1000; i++)
    {
      int index = i;
      mailbox.Post(() => received.Add(index));
    }

    Assert.Single(scheduled);
    Assert.Empty(received); // producers never execute WinForms callbacks
    int uiTicks = 0;
    while (scheduled.TryDequeue(out var callback))
    {
      callback();
      uiTicks++;
      Assert.True(uiTicks < 1000);
    }
    Assert.True(uiTicks > 1, "UI batches must yield to spectrum rendering");
    Assert.Equal(Enumerable.Range(0, 1000), received);
    Assert.Equal(0, mailbox.PendingCount);
  }

  [Fact]
  public void MailboxDoesNotLoseCallbacksPostedDuringDrain()
  {
    var scheduled = new Queue<Action>();
    var mailbox = new TelemetryUiMailbox(scheduled.Enqueue);
    int received = 0;
    mailbox.Post(() =>
    {
      received++;
      mailbox.Post(() => received++);
    });

    while (scheduled.Count > 0) scheduled.Dequeue()();
    Assert.Equal(2, received);
    Assert.Equal(0, mailbox.PendingCount);
  }

  [Fact]
  public void IntermediateFmTranscriptsAreCoalescedButCompletedEventsStayOrdered()
  {
    var scheduled = new Queue<Action>();
    var received = new List<string>();
    var mailbox = new TelemetryUiMailbox(scheduled.Enqueue);
    var latest = new TelemetryLatestUiValue<string>(mailbox,
      line => received.Add("pending: " + line));

    for (int i = 0; i < 1000; i++) latest.Offer("word-" + i);
    mailbox.Post(() => received.Add("completed"));

    Assert.Single(scheduled);
    while (scheduled.Count > 0) scheduled.Dequeue()();
    Assert.Equal(new[] { "pending: word-999", "completed" }, received);
  }

  [Fact]
  public void ClosingPanelDropsOnlyUnrenderedUiWork()
  {
    var scheduled = new Queue<Action>();
    var mailbox = new TelemetryUiMailbox(scheduled.Enqueue);
    int hit = 0;
    mailbox.Post(() => hit++);
    mailbox.Close();
    while (scheduled.Count > 0) scheduled.Dequeue()();
    mailbox.Post(() => hit++);
    Assert.Equal(0, hit);
    Assert.Equal(0, mailbox.PendingCount);
  }

  [Theory]
  [InlineData(true, false, true)]  // switching away from active FM
  [InlineData(true, true, false)]  // old decoder released
  [InlineData(false, false, false)] // telemetry-only branch does not use model
  public void SharedFmModelRequiresPreviousDecoderToFinish(
    bool engineReady, bool previousFmRetired, bool expectedDefer)
  {
    Assert.Equal(expectedDefer, TelemetryPanel.ShouldDeferFmDecoder(
      engineReady, previousFmRetired));
  }

  [Theory]
  [InlineData(false, false, TelemetryReceiveSource.SdrIq, false, false)]
  [InlineData(false, false, TelemetryReceiveSource.SdrIq, true, false)]
  [InlineData(false, false, TelemetryReceiveSource.Capture12kIf, false, false)]
  [InlineData(false, false, TelemetryReceiveSource.Capture12kIf, true, true)]
  [InlineData(false, false, TelemetryReceiveSource.Shared12kIf, true, true)]
  [InlineData(false, false, TelemetryReceiveSource.IfAfDemodulatedAf, true, true)]
  [InlineData(false, true, TelemetryReceiveSource.SdrIq, false, true)]
  [InlineData(true, false, TelemetryReceiveSource.SdrIq, false, true)]
  public void ManualIfAfTelemetryMustNotBeBlockedByStaleHorizonStatus(
    bool aboveHorizon, bool replayActive, TelemetryReceiveSource inputSource,
    bool audioReady, bool expected)
  {
    Assert.Equal(expected, TelemetryPanel.ShouldBuildSatelliteDecoder(
      aboveHorizon, replayActive, inputSource, audioReady));
  }

  [Fact]
  public void DecoderRetirementNeverWaitsForAnActiveWorkerOnCallingThread()
  {
    var retirement = new TelemetryDecoderRetirement();
    using var workerStarted = new ManualResetEventSlim();
    using var allowShutdown = new ManualResetEventSlim();
    int state = 0;
    retirement.Enqueue(() =>
    {
      workerStarted.Set();
      allowShutdown.Wait(TimeSpan.FromSeconds(10));
      Interlocked.Exchange(ref state, 1);
    });
    retirement.Enqueue(() => Assert.Equal(1, Volatile.Read(ref state)));

    try
    {
      Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));
      Assert.False(retirement.Completion.IsCompleted);
    }
    finally { allowShutdown.Set(); }

    Assert.True(retirement.Completion.Wait(TimeSpan.FromSeconds(10)));
    Assert.Equal(1, Volatile.Read(ref state));
  }
}
