namespace SkyRoof.CW
{
  public sealed partial class CwReceiveWorker
  {
    private async Task TrackingLoopAsync(
      CancellationToken cancellationToken)
    {
      using var timer =
        new PeriodicTimer(
          options.TrackingInterval);

      HandleTimelineGeneration();

      while (await timer.WaitForNextTickAsync(
        cancellationToken)
        .ConfigureAwait(false))
      {
        HandleTimelineGeneration();

        if (!ingress.Enabled)
          continue;

        try
        {
          ApplyTrackingMode();
          IReadOnlyList<CwSignalTrack> tracks =
            trackAnalyzer(
              Volatile.Read(
                ref knownDopplerRateHzPerSecond));

          CwSignalTrack[] snapshot =
            tracks.ToArray();

          lock (sync)
            latestTracks = snapshot;

          Interlocked.Increment(
            ref trackingCycles);

          TracksUpdated?.Invoke(
            this,
            new CwTracksUpdatedEventArgs(
              observedGeneration,
              snapshot));

          TryScheduleInference(
            snapshot);
        }
        catch (Exception ex)
        {
          SetError(ex);
        }
      }
    }

    private void HandleTimelineGeneration()
    {
      long generation =
        ingress.TimelineGeneration;
      if (generation ==
          observedGeneration)
        return;

      observedGeneration =
        generation;
      lastInferenceAttemptSampleIndex =
        -1;
      Interlocked.Exchange(
        ref pendingSessionReset,
        1);

      lock (sync)
      {
        latestTracks =
          Array.Empty<CwSignalTrack>();
        latestDecode = null;
      }

      OnStatusChanged();
    }

    private void ApplyTrackingMode()
    {
      CwDecoderMode selected;
      long revision;
      lock(sync)
      {
        revision=modeRevision;
        if(appliedTrackingModeRevision==revision)return;
        appliedTrackingModeRevision=revision;
        selected=decoderMode;
      }
      CwPileupFrontEnd front=ingress.FrontEnd;
      front.EnableCarrierPhaseVerification=selected==CwDecoderMode.NewCW;
      front.EnableExperimentalStationGate=selected==CwDecoderMode.NewCW;
      front.ResetVerification();
    }
  }
}
