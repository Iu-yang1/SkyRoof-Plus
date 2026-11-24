using System.Diagnostics;
using SkyRoof.Orbit.Contracts;

namespace SkyRoof
{
  public partial class RadioLink
  {
    internal const double SpeedOfLightKmPerSecond = 299_792.458;
    public SatnogsDbSatellite? Sat;
    public SatnogsDbTransmitter? Tx;
    public SatelliteCustomization? SatCust;
    public TransmitterCustomization? TxCust;

    // A satellite selection must install both customization objects before its
    // persistent controls are read or written. Fail explicitly rather than
    // dereferencing missing state and leaving the tune cycle half-applied.
    private TransmitterCustomization RequiredTransmitterCustomization =>
      TxCust ?? throw new InvalidOperationException(
        "Transmitter customization is not initialized for this radio link");

    private SatelliteCustomization RequiredSatelliteCustomization =>
      SatCust ?? throw new InvalidOperationException(
        "Satellite customization is not initialized for this radio link");

    // non-persistent
    public bool IsTerrestrial = true;
    public bool RitEnabled;
    public double RitOffset;
    public double XitOffset;

    // persistent
    public Slicer.Mode DownlinkMode 
    { 
      get => RequiredTransmitterCustomization.DownlinkMode; 
      set => RequiredTransmitterCustomization.DownlinkMode = value; 
    }
    public Slicer.Mode UplinkMode 
    { 
      get => RequiredTransmitterCustomization.UplinkMode; 
      set => RequiredTransmitterCustomization.UplinkMode = value; 
    }
    public double TransponderOffset
    {
      get => RequiredTransmitterCustomization.TransponderOffset;
      set => RequiredTransmitterCustomization.TransponderOffset = value;
    }
    public long DownlinkBaseOffset
    {
      get => RequiredTransmitterCustomization.DownlinkBaseOffset;
      set => RequiredTransmitterCustomization.DownlinkBaseOffset = value;
    }
    public long UplinkBaseOffset
    {
      get => RequiredTransmitterCustomization.UplinkBaseOffset;
      set => RequiredTransmitterCustomization.UplinkBaseOffset = value;
    }
    public double DatabaseDownlinkBaseFrequency => Tx?.downlink_low ?? 0;
    public double DatabaseUplinkBaseFrequency =>
      Tx?.uplink_low == null ? 0 :
      Tx.invert && Tx.uplink_high.HasValue ? Tx.uplink_high.Value : Tx.uplink_low.Value;
    public double BaseDownlinkFrequency => DatabaseDownlinkBaseFrequency + DownlinkBaseOffset;
    public double BaseUplinkFrequency =>
      DatabaseUplinkBaseFrequency == 0 ? 0 : DatabaseUplinkBaseFrequency + UplinkBaseOffset;

    public double CtcssTone
    {
      get => RequiredTransmitterCustomization.CtcssTone;
      set => RequiredTransmitterCustomization.CtcssTone = value;
    }
    public bool CtcssEnabled
    {
      get => RequiredTransmitterCustomization.CtcssEnabled;
      set => RequiredTransmitterCustomization.CtcssEnabled = value;
    }
    public double DownlinkManualCorrection
    {
      get => RequiredSatelliteCustomization.DownlinkManualCorrection;
      set => RequiredSatelliteCustomization.DownlinkManualCorrection = (int)value;
    }
    public double UplinkManualCorrection
    {
      get => RequiredSatelliteCustomization.UplinkManualCorrection;
      set => RequiredSatelliteCustomization.UplinkManualCorrection = (int)value;
    }

    public bool DownlinkDopplerCorrectionEnabled 
    { 
      get => RequiredSatelliteCustomization.DownlinkDopplerCorrectionEnabled; 
      set => RequiredSatelliteCustomization.DownlinkDopplerCorrectionEnabled = value; 
    }
    public bool UplinkDopplerCorrectionEnabled
    {
      get => RequiredSatelliteCustomization.UplinkDopplerCorrectionEnabled;
      set => RequiredSatelliteCustomization.UplinkDopplerCorrectionEnabled = value;
    }
    public bool DownlinkManualCorrectionEnabled
    {
      get => RequiredSatelliteCustomization.DownlinkManualCorrectionEnabled;
      set => RequiredSatelliteCustomization.DownlinkManualCorrectionEnabled = value;
    }
    public bool UplinkManualCorrectionEnabled
    {
      get => RequiredSatelliteCustomization.UplinkManualCorrectionEnabled;
      set => RequiredSatelliteCustomization.UplinkManualCorrectionEnabled = value;
    }

    // computed
    public double DownlinkFrequency, CorrectedDownlinkFrequency;
    public double UplinkFrequency, CorrectedUplinkFrequency;

    // What the radio frequency would be if Doppler correction alone were disabled.
    // Base/transponder position and operator corrections remain included.
    public double DownlinkFrequencyWithoutDoppler =>
      !IsTerrestrial && !HasDownlink
        ? 0
        : DownlinkFrequency +
          (RitEnabled ? RitOffset : 0) +
          (!IsTerrestrial && SatCust != null && DownlinkManualCorrectionEnabled
            ? DownlinkManualCorrection : 0);

    public double UplinkFrequencyWithoutDoppler =>
      IsTerrestrial || UplinkFrequency <= 0 ? 0 :
      UplinkFrequency +
      (SatCust != null && UplinkManualCorrectionEnabled ? UplinkManualCorrection : 0) +
      XitOffset;

    public double DopplerFactor = 0;
    public bool IsAboveHorizon;
    // true when the propagator returned a valid observation this tick (independent of elevation);
    // false means DopplerFactor was forced to 0, a discontinuity the rate estimator must not ramp
    public bool HasObservation;

    // estimates the doppler rate for the Slicer's continuous correction (see DopplerRateEstimator)
    private readonly DopplerRateEstimator DopplerEstimator = new();

    // downlink offset doppler rate (Hz/s) for the Slicer's continuous correction; 0 when correction
    // is off. CorrectedDownlinkFrequency *= (1 - DopplerFactor), so the offset's doppler term is
    // -DownlinkFrequency * DopplerFactor and its time derivative is -DownlinkFrequency * factorRate.
    public double DownlinkDopplerRate =>
      DownlinkDopplerCorrectionEnabled ? -DownlinkFrequency * DopplerEstimator.FactorRate : 0;
    public bool HasDownlink =>
      IsTerrestrial ||
      Tx?.downlink_low.HasValue == true;
    public bool HasUplink =>
      !IsTerrestrial &&
      UplinkFrequency > 0 &&
      SatnogsDbTransmitter.IsHamFrequency(UplinkFrequency);
    public bool IsTransponder => Tx != null &&
      Tx.downlink_high.HasValue && Tx.downlink_high != Tx.downlink_low &&
      Tx.uplink_low.HasValue && Tx.uplink_high.HasValue;
    public bool IsCrossBand => HasDownlink && HasUplink &&
      SatnogsDbTransmitter.GetAmateurBand(UplinkFrequency) !=
      SatnogsDbTransmitter.GetAmateurBand(DownlinkFrequency);


    public void ObserveSatellite(SatellitePasses engine)
    {
      var now = DateTime.UtcNow;
      var observation = engine.ObserveSatellite(Sat, now);

      if (observation == null)
      {
        DopplerFactor = 0;
        IsAboveHorizon = false;
        HasObservation = false;
      }
      else
      {
        DopplerFactor = observation.RangeRate / SpeedOfLightKmPerSecond;
        IsAboveHorizon = observation.Elevation > 0;
        HasObservation = true;
      }

      // runs only here, the one place DopplerFactor is refreshed, so the estimate cannot be
      // desynced by the many other callers of ComputeFrequencies
      DopplerEstimator.Update(DopplerFactor, HasObservation, Sat, now);
    }

    private SatellitePassband Passband => new(
      Tx?.downlink_low, Tx?.downlink_high, Tx?.uplink_low, Tx?.uplink_high, Tx?.invert == true);

    internal void ComputeFrequencies()
    {
      if (IsTerrestrial)
      {
        CorrectedDownlinkFrequency = DownlinkFrequency;
        if (RitEnabled) CorrectedDownlinkFrequency += RitOffset;
        CorrectedUplinkFrequency = UplinkFrequency = 0;
        DopplerFactor = 0;
      }

      else
      {
        // A local record may intentionally be uplink-only. Do not synthesize a
        // 0-Hz downlink or disturb RX/SDR state for such a record.
        if (Tx?.downlink_low.HasValue == true)
        {
          DownlinkFrequency = Passband.DownlinkNominal(
            DownlinkBaseOffset, IsTransponder ? TransponderOffset : 0);
          CorrectedDownlinkFrequency = RadioLinkMath.CorrectDownlink(
            DownlinkFrequency, DownlinkDopplerCorrectionEnabled ? DopplerFactor : 0,
            RitEnabled ? RitOffset : 0,
            DownlinkManualCorrectionEnabled ? DownlinkManualCorrection : 0);
        }
        else
        {
          DownlinkFrequency = 0;
          CorrectedDownlinkFrequency = 0;
          RitEnabled = false;
          RitOffset = 0;
        }

        // uplink nominal. Apply the same base offset to both passband edges so its width is unchanged.
        UplinkFrequency = Tx?.uplink_low.HasValue == true
          ? Passband.UplinkNominal(UplinkBaseOffset, IsTransponder ? TransponderOffset : 0)
          : 0;

        CorrectedUplinkFrequency = UplinkFrequency > 0
          ? RadioLinkMath.CorrectUplink(UplinkFrequency,
            UplinkDopplerCorrectionEnabled ? DopplerFactor : 0, XitOffset,
            UplinkManualCorrectionEnabled ? UplinkManualCorrection : 0)
          : UplinkFrequency;
      }
    }
  }
}
