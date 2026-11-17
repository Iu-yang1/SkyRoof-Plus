using System;
using Serilog;
using VE3NEA;

namespace SkyRoof.IF
{
  public sealed partial class IfAfService
  {
    /// <summary>48-kHz post-demodulation AF before voice-only enhancement
    /// and output leveling. Subscribers copy pooled data synchronously.</summary>
    public event EventHandler<DataEventArgs<float>>? CleanDemodulatedAfAvailable;

    private void PublishCleanAf(EventHandler<DataEventArgs<float>> listener,
      float[] samples, int count, DateTime utc)
    {
      try
      {
        listener.Invoke(this, new DataEventArgs<float>
        {
          Data = samples, Count = count, Utc = utc
        });
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "IF-AF clean PCM subscriber failed");
      }
    }
  }
}
