using System;
using System.Threading;

namespace SkyRoof
{
  public partial class TelemetryPanel
  {
    private readonly TelemetryDecoderRetirement decoderRetirement = new();
    private TelemetryUiMailbox? telemetryUi;
    private int audioConfigurationGeneration;
    private int discontinuityResetPending;
    private bool telemetryClosing;
    // Sherpa/ONNX is shared by successive FM decoders, so its previous
    // streaming instance must finish Flush/Dispose before another uses it.
    private Task fmDecoderRestartReady = Task.CompletedTask;

    /// <summary>
    /// Remove the current decoder from ingress on the UI thread, then retire
    /// its worker and native resources asynchronously. No UI call may join
    /// the FFT/deframer worker or flush an in-flight burst.
    /// </summary>
    private void RetireActiveDecoder()
    {
      var old = Decoder;
      Decoder = null;
      CurrentDecode = null;
      if (old == null) return;
      bool wasFm = old.Fm != null;
      decoderRetirement.Retire(old);
      if (!wasFm) return;

      var retired = decoderRetirement.Completion;
      fmDecoderRestartReady = retired;
      _ = Task.Run(async () =>
      {
        await retired.ConfigureAwait(false);
        if (telemetryClosing) return;
        telemetryUi?.Post(() =>
        {
          if (!telemetryClosing && ReferenceEquals(fmDecoderRestartReady, retired))
            UpdateTxStatus();
        });
      });
    }

    internal static bool ShouldDeferFmDecoder(bool engineReady, bool retired) =>
      engineReady && !retired;

    private void QueueDiscontinuityReset()
    {
      if (telemetryClosing || telemetryUi == null) return;
      int generation = Volatile.Read(ref audioConfigurationGeneration);
      if (Interlocked.Exchange(ref discontinuityResetPending, 1) != 0) return;
      telemetryUi.Post(() =>
      {
        Interlocked.Exchange(ref discontinuityResetPending, 0);
        if (telemetryClosing ||
            generation != Volatile.Read(ref audioConfigurationGeneration)) return;
        RetireActiveDecoder();
        CreatDestroyPipeline();
      });
    }
  }
}
