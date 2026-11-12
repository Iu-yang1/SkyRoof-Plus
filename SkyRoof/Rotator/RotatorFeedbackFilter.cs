using System;
using VE3NEA;

namespace SkyRoof;

/// <summary>
/// Keeps the last independently accepted controller bearing separate from raw
/// telemetry. Rejected samples do not change the control reference. Motion
/// targets may continue while the controller is responding with outliers.
/// </summary>
internal sealed class RotatorFeedbackFilter
{
  private readonly double jumpLimit;
  private Bearing? recoveryCandidate;
  private int recoveryCount;

  internal RotatorFeedbackFilter(double jumpLimitDegrees) =>
    jumpLimit = Math.Clamp(jumpLimitDegrees, 5.0, 180.0);

  internal Bearing? Trusted { get; private set; }
  internal Bearing? Rejected { get; private set; }
  internal DateTime LastTrustedUtc { get; private set; }
  internal DateTime DegradedSinceUtc { get; private set; }
  internal bool IsDegraded { get; private set; }
  internal bool AzimuthSuspect { get; private set; }
  internal bool ElevationSuspect { get; private set; }

  internal void Reset()
  {
    Trusted = Rejected = recoveryCandidate = null;
    LastTrustedUtc = DegradedSinceUtc = DateTime.MinValue;
    IsDegraded = AzimuthSuspect = ElevationSuspect = false;
    recoveryCount = 0;
  }

  internal static double AzimuthDifference(double left, double right)
  {
    bool extended = left < 0 || left > 360 || right < 0 || right > 360;
    if (extended) return Math.Abs(left - right);
    double a = ((left % 360) + 360) % 360;
    double b = ((right % 360) + 360) % 360;
    double raw = Math.Abs(a - b);
    return Math.Min(raw, 360 - raw);
  }

  internal bool Observe(Bearing sample, DateTime utc, Bearing? commandedTarget)
  {
    if (Trusted == null ||
        !IsDegraded && (utc - LastTrustedUtc).TotalSeconds > 2.0)
    {
      Accept(sample, utc);
      return true;
    }

    double azJump = AzimuthDifference(Trusted.AzDeg, sample.AzDeg);
    double elJump = Math.Abs(Trusted.ElDeg - sample.ElDeg);
    if (!IsDegraded)
    {
      if (azJump <= jumpLimit && elJump <= jumpLimit)
      {
        Accept(sample, utc);
        return true;
      }

      IsDegraded = true;
      DegradedSinceUtc = utc;
      AzimuthSuspect = azJump > jumpLimit;
      ElevationSuspect = elJump > jumpLimit;
      recoveryCandidate = null;
      recoveryCount = 0;
      Rejected = sample;
      return false;
    }

    // The last trusted bearing is a reference, not a permanent physical
    // position. Accept a new cluster either near that reference or near the
    // latest commanded target after two mutually consistent replies.
    double targetTolerance = Math.Clamp(jumpLimit / 2.0, 5.0, 15.0);
    bool nearReference = azJump <= jumpLimit && elJump <= jumpLimit;
    bool nearCommand = commandedTarget != null &&
      AzimuthDifference(commandedTarget.AzDeg, sample.AzDeg) <= targetTolerance &&
      Math.Abs(commandedTarget.ElDeg - sample.ElDeg) <= targetTolerance;
    bool consistent = recoveryCandidate == null ||
      AzimuthDifference(recoveryCandidate.AzDeg, sample.AzDeg) <= targetTolerance &&
      Math.Abs(recoveryCandidate.ElDeg - sample.ElDeg) <= targetTolerance;

    if ((nearReference || nearCommand) && consistent)
    {
      recoveryCount++;
      recoveryCandidate = sample;
      if (recoveryCount >= 2)
      {
        Accept(sample, utc);
        return true;
      }
    }
    else
    {
      recoveryCount = 0;
      recoveryCandidate = null;
    }

    Rejected = sample;
    return false;
  }

  private void Accept(Bearing bearing, DateTime utc)
  {
    Trusted = bearing;
    LastTrustedUtc = utc;
    Rejected = recoveryCandidate = null;
    recoveryCount = 0;
    DegradedSinceUtc = DateTime.MinValue;
    IsDegraded = AzimuthSuspect = ElevationSuspect = false;
  }
}
