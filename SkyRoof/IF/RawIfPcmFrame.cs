using System;

namespace SkyRoof.IF
{
  /// <summary>Continuity-aware raw 48-kHz real IF PCM.</summary>
  public readonly record struct RawIfPcmFrame(
    float[] Samples, DateTime Utc, long FirstSampleIndex, bool Discontinuity);
}
