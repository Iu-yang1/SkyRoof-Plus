using System;

namespace SkyRoof
{
  public enum AfcSignalLossPolicy { HoldLast, GentleRelease }
}

namespace SkyRoof.AFC
{
  public enum AfcSignalMode { FM, CW, SSB }
  public enum AfcControlMode { Off, Assist, Auto }
  public enum AfcLockState { Off, Acquiring, Locked, Coasting, Hold, Lost, Fault }

  public readonly record struct AfcObservation(
    AfcSignalMode Mode,
    DateTime SampleUtc,
    long Generation,
    double IfOffsetHz,
    double SigmaHz,
    double Confidence,
    bool HasAbsoluteReference,
    bool Ambiguous = false,
    float[]? SpectrumDb = null,
    double SpectrumMinHz = -6000,
    double SpectrumStepHz = 12000.0 / 511);

  /// <summary>
  /// Immutable time-aligned CAT/control context for one AFC observation.
  /// Defaults match the legacy Observe overload and do not change servo math.
  /// </summary>
  public readonly record struct AfcObservationContext(
    double CatReadbackHz,
    int ConfiguredSign,
    double MaxCorrectionHz,
    double FmDeadbandHz,
    double ToneDeadbandHz,
    bool Transmitting,
    bool CatReadbackIsTimeAligned = true,
    bool ConfirmedOwnAfcReadback = false,
    bool CalibrationInProgress = false,
    int NominalSign = 0);

  public readonly record struct AfcSnapshot(
    AfcLockState State,
    AfcSignalMode Mode,
    AfcControlMode Control,
    double ObservedIfOffsetHz,
    double CorrectionHz,
    double ErrorHz,
    double Confidence,
    int MappingSign,
    DateTime LastObservationUtc,
    string Detail);
}
