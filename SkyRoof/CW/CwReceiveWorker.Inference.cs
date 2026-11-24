namespace SkyRoof.CW
{
  public sealed partial class CwReceiveWorker
  {
    private void TryScheduleInference(
      IReadOnlyList<CwSignalTrack> tracks)
    {
      if (!tracks.Any(x => x.Confirmed))
        return;

      CwAudioHub audio =
        ingress.FrontEnd.Audio;
      long totalSamples =
        audio.TotalSamplesWritten;
      long windowSamples =
        checked((long)Math.Round(
          options.DecodeWindowSeconds *
          audio.SampleRate));
      long hopSamples =
        checked((long)Math.Round(
          options.DecodeHopSeconds *
          audio.SampleRate));

      if (totalSamples < windowSamples)
        return;

      bool due =
        lastInferenceAttemptSampleIndex < 0 ||
        totalSamples -
          lastInferenceAttemptSampleIndex >=
          hopSamples;
      if (!due)
        return;

      // Mark this hop consumed before checking Busy. A slow model must not
      // create a backlog of all missed windows.
      lastInferenceAttemptSampleIndex =
        totalSamples;

      if (Interlocked.CompareExchange(
            ref inferenceBusy,
            1,
            0) != 0)
      {
        Interlocked.Increment(
          ref skippedInferenceWindows);
        OnStatusChanged();
        return;
      }

      if (!audio.TrySnapshot(
            options.DecodeWindowSeconds,
            out CwAudioSnapshot pcm))
      {
        Interlocked.Exchange(
          ref inferenceBusy,
          0);
        return;
      }

      CwSignalTrack[] trackSnapshot =
        tracks.ToArray();
      long generation =
        observedGeneration;
      long revision;
      lock(sync)revision=modeRevision;

      Task task =
        Task.Run(
          () => RunInference(
            pcm,
            trackSnapshot,
            generation,revision));

      lock (sync)
        inferenceTask = task;

      OnStatusChanged();
    }

    private void RunInference(
      CwAudioSnapshot pcm,
      IReadOnlyList<CwSignalTrack> tracks,
      long generation,long revision)
    {
      try
      {
        if (generation !=
            ingress.TimelineGeneration ||
            revision!=Interlocked.Read(ref modeRevision))
          return;

        ICwInferenceSession? decoder =
          GetOrCreateSession();
        if (decoder == null)
          return;

        if (Interlocked.Exchange(
              ref pendingSessionReset,
              0) != 0)
          decoder.Reset();

        if (generation !=
            ingress.TimelineGeneration ||
            revision!=Interlocked.Read(ref modeRevision))
          return;

        CwContinuousDecodeBatch batch =
          decoder.Decode(
            pcm,
            tracks);

        Interlocked.Increment(
          ref completedInferenceWindows);

        if (generation !=
            ingress.TimelineGeneration ||
            revision!=Interlocked.Read(ref modeRevision))
          return;

        CwDecoderMode completedMode;
        lock(sync)
        {
          // Mode switches may arrive after the prior unlocked generation
          // check. Publish and archive atomically with the mode revision
          // so a late ONNX reply cannot pollute a New CW transcript.
          if(revision!=modeRevision ||
             generation!=ingress.TimelineGeneration)
            return;
          latestDecode=batch;
          completedMode=decoderMode;
          History.Append(generation,batch.Transcripts,completedMode);
        }

        DecodeUpdated?.Invoke(
          this,
          new CwDecodeUpdatedEventArgs(
            generation,
            batch,completedMode));
      }
      catch (Exception ex)
      {
        SetError(ex);

        lock (sync)
        {
          session?.Dispose();
          session = null;
          sessionMode=null;
          modelState =
            CwReceiveModelState.Error;
          nextModelAttemptUtc =
            DateTime.UtcNow +
            options.ModelRetryInterval;
        }
      }
      finally
      {
        Interlocked.Exchange(
          ref inferenceBusy,
          0);
        OnStatusChanged();
      }
    }

    private ICwInferenceSession?
      GetOrCreateSession()
    {
      lock (sync)
      {
        if (session!=null && sessionMode!=decoderMode)
        {
          session.Dispose();
          session=null;
          sessionMode=null;
          nextModelAttemptUtc=DateTime.MinValue;
        }
        if (session != null)
          return session;

        DateTime now =
          DateTime.UtcNow;
        if (now < nextModelAttemptUtc)
          return null;

        ICwInferenceSession? created =
          sessionFactory(decoderMode);
        if (created == null)
        {
          modelState =
            CwReceiveModelState.NotInstalled;
          nextModelAttemptUtc =
            now +
            options.ModelRetryInterval;
          lastError = null;
          return null;
        }

        session = created;
        sessionMode=decoderMode;
        modelState =
          CwReceiveModelState.Ready;
        nextModelAttemptUtc =
          DateTime.MinValue;
        lastError = null;
        return session;
      }
    }
  }
}
