using Serilog;
using VE3NEA;

namespace SkyRoof
{
  public class SdrSatellitePasses : SatellitePasses
  {
    private double StartFrequency, EndFrequency;
    private long rebuildVersion;
    private CancellationTokenSource? rebuildCancellation;

    public SdrSatellitePasses(Context ctx) : base(ctx)
    {
      PredictionTimeSpan = TimeSpan.FromMinutes(6);
      UpdateFrequencyRange();
    }

    public void UpdateFrequencyRange()
    {
      double startFrequency = 0;
      double endFrequency = 0;

      if (ctx.WaterfallPanel != null && ctx.Sdr != null &&
        !SatnogsDbTransmitter.IsHamFrequency(ctx.FrequencyControl.GetSdrRfCenter()))
      {
        double centerFrequency = ctx.FrequencyControl.GetSdrRfCenter();
        double wing = ctx.Sdr.Info.MaxBandwidth / 2;
        startFrequency = centerFrequency - wing;
        endFrequency = centerFrequency + wing;
      }

      if (startFrequency == StartFrequency && endFrequency == EndFrequency) return;

      StartFrequency = startFrequency;
      EndFrequency = endFrequency;

      QueueRangeRebuild();
    }

    private void CancelPendingRangeRebuild()
    {
      ++rebuildVersion;
      rebuildCancellation?.Cancel();
      rebuildCancellation = null;
    }

    public override void FullRebuild()
    {
      CancelPendingRangeRebuild();
      base.FullRebuild();
    }

    public override void Rebuild()
    {
      CancelPendingRangeRebuild();
      base.Rebuild();
    }

    private void QueueRangeRebuild()
    {
      CancelPendingRangeRebuild();
      long generation = rebuildVersion;
      if (StartFrequency == 0 || EndFrequency == 0)
      {
        ClearPendingGroupPasses();
        return;
      }

      var source = ListSatellites().ToArray();
      ClearPendingGroupPasses();
      var cancellation = new CancellationTokenSource();
      rebuildCancellation = cancellation;
      RebuildRangeAsync(source, generation, cancellation).DoNotAwait();
    }

    private async Task RebuildRangeAsync(
      SatnogsDbSatellite[] source, long generation, CancellationTokenSource cancellation)
    {
      try
      {
        var snapshot = await Task.Run(() =>
          ComputeFullRebuildSnapshot(source, cancellation.Token), cancellation.Token);
        if (cancellation.IsCancellationRequested || generation != rebuildVersion ||
            !ReferenceEquals(ctx.SdrPasses, this))
          return;
        ApplyFullRebuildSnapshot(snapshot);
      }
      catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
      catch (Exception ex)
      {
        Log.Warning(ex, "Background SDR pass prediction failed.");
      }
      finally
      {
        if (ReferenceEquals(rebuildCancellation, cancellation))
          rebuildCancellation = null;
        cancellation.Dispose();
      }
    }

    protected override IEnumerable<SatnogsDbSatellite> ListSatellites()
    {
      if (StartFrequency == 0 || EndFrequency == 0) return Array.Empty<SatnogsDbSatellite>();

      return ctx.SatnogsDb.Satellites.Where(sat =>
        sat.Transmitters.Any(tx => tx.DownlinkLow >= StartFrequency) &&
        sat.Transmitters.Any(tx => tx.DownlinkLow <= EndFrequency));
    }
}


}
