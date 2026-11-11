using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using VE3NEA;
using SkyRoof;

namespace SkyRoof.IF
{
  public sealed partial class IfAfService
  {
    internal void AcceptNetworkIfAudio(
      int inputRate, IcomLanLpcmAudioCodec.RxFrame packet)
    {
      if (disposed || activeInputSource != IfAfInputSource.AuthenticatedLanIf ||
          settings?.Enabled != true || queue == null)
        return;

      lock (networkSync)
      {
        foreach (IcomNetworkIfBlock block in networkPcm.Push(
                   inputRate, packet, DateTime.UtcNow))
          AcceptNetworkIfBlock(block);
      }
    }

    private async Task FlushNetworkIfAsync(CancellationToken stop)
    {
      using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
      try
      {
        while (await timer.WaitForNextTickAsync(stop))
        {
          lock (networkSync)
          {
            foreach (IcomNetworkIfBlock block in
                     networkPcm.Flush(DateTime.UtcNow))
              AcceptNetworkIfBlock(block);
          }
        }
      }
      catch (OperationCanceledException) when (stop.IsCancellationRequested)
      {
      }
    }

    internal void NetworkIfDisconnected()
    {
      if (activeInputSource != IfAfInputSource.AuthenticatedLanIf)
        return;
      lock (networkSync)
      {
        networkPcm.Reset();
        Interlocked.Exchange(ref lastUtcTicks, 0);
        detail = "Network IF disconnected; awaiting Direct LAN Scope reconnect";
      }
    }

    private void AcceptNetworkIfBlock(IcomNetworkIfBlock block)
    {
      var writer = queue?.Writer;
      if (writer == null || block.Samples.Length == 0)
        return;

      var dataEvent = new DataEventArgs<float>
      {
        Data = block.Samples,
        Count = block.Samples.Length,
        Utc = block.Utc
      };
      try { RawIfAvailable?.Invoke(this, dataEvent); }
      catch (Exception ex) { Log.Warning(ex, "Network IF shared consumer failed"); }

      long index = Interlocked.Add(ref inputSamples, block.Samples.Length) -
        block.Samples.Length;
      Interlocked.Exchange(ref lastUtcTicks, block.Utc.Ticks);
      if (!writer.TryWrite(new Frame(block.Samples, block.Utc,
            generation, index, block.Discontinuity)))
      {
        Interlocked.Increment(ref dropped);
        detail = "Network IF PCM queue overrun; resetting DSP on next gap";
        networkPcm.Reset();
      }
      else
      {
        detail = "Authenticated network 48-kHz IF -> virtual AF";
      }
    }

  }
}
