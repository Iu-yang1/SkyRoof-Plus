namespace SkyRoof
{
  public partial class RadioLink
  {
    public void SetDownlinkBaseFrequency(double frequency)
    {
      if (IsTerrestrial ||
          !HasDownlink ||
          Tx == null ||
          TxCust == null)
        return;
      DownlinkBaseOffset = checked((long)Math.Round(frequency - DatabaseDownlinkBaseFrequency));
      ComputeFrequencies();
    }

    public void SetUplinkBaseFrequency(double frequency)
    {
      if (IsTerrestrial || Tx == null || TxCust == null || DatabaseUplinkBaseFrequency == 0) return;
      UplinkBaseOffset = checked((long)Math.Round(frequency - DatabaseUplinkBaseFrequency));
      ComputeFrequencies();
    }

    public void ResetDownlinkBaseFrequency()
    {
      if (TxCust == null) return;
      DownlinkBaseOffset = 0;
      ComputeFrequencies();
    }

    public void ResetUplinkBaseFrequency()
    {
      if (TxCust == null) return;
      UplinkBaseOffset = 0;
      ComputeFrequencies();
    }

    // dragging changes either the absolute frequency (terrestrial),
    // or the transponder offset (transponder),
    // or the manual correction (transmitter)
    internal double GetDraggableFrequency()
    {
      if (!HasDownlink)
        return 0;
      if (IsTerrestrial)
        return DownlinkFrequency;
      if (IsTransponder)
        return TransponderOffset;
      return DownlinkManualCorrection;
    }

    internal void SetDraggableFrequency(double freq)
    {
      if (!HasDownlink)
        return;

      if (IsTerrestrial)
        DownlinkFrequency = freq;

      else if (IsTransponder)
      {
        // Do not clamp to the SatNOGS passband. Published transponder/IF edges can be
        // approximate, and operators may need to tune beyond them.
        TransponderOffset = freq;
      }

      else
      {
        DownlinkManualCorrection = freq;
      }

      ComputeFrequencies();
    }

    internal void IncrementDownlinkFrequency(int delta)
    {
      if (!HasDownlink)
        return;

      // RIT
      if (RitEnabled)
      {
        RitOffset += delta;
      }

      // terrestrial
      else if (IsTerrestrial) 
        DownlinkFrequency += delta;

      // transponder
      else if (IsTransponder)
      {
        // The tuning position is intentionally unbounded by the database passband edges.
        TransponderOffset += delta;
      }

      // transmitter
      else
      {
        DownlinkManualCorrection += delta;
      }

      ComputeFrequencies();
    }

    /// <summary>
    /// Move the logical receive point to an absolute corrected RF frequency.
    /// This solves the same frequency model used by ComputeFrequencies instead
    /// of treating the requested corrected-frequency delta as a raw model delta.
    /// </summary>
    internal bool SetCorrectedDownlinkFrequency(
      double targetFrequency,
      bool useRit)
    {
      if (!CanSetCorrectedDownlink(targetFrequency, useRit))
        return false;

      bool ritModeChanged =
        RitEnabled != useRit;
      double previousCorrected =
        CorrectedDownlinkFrequency;

      RitEnabled =
        useRit;

      if (IsTerrestrial)
      {
        if (useRit)
          RitOffset =
            targetFrequency -
            DownlinkFrequency;
        else
          DownlinkFrequency =
            targetFrequency;

        ComputeFrequencies();

        return
          ritModeChanged ||
          Math.Abs(
            CorrectedDownlinkFrequency -
            previousCorrected) >= 0.5;
      }

      if (!SetSatelliteTuningPosition(targetFrequency, useRit)) return false;

      ComputeFrequencies();

      return
        ritModeChanged ||
        Math.Abs(
          CorrectedDownlinkFrequency -
          previousCorrected) >= 0.5;
    }


    private bool SetSatelliteTuningPosition(double targetFrequency, bool useRit)
    {
      double dopplerScale =
        DownlinkDopplerCorrectionEnabled
          ? 1.0 - DopplerFactor
          : 1.0;

      if (!double.IsFinite(
            dopplerScale) ||
          Math.Abs(dopplerScale) <
            1e-9)
        return false;

      double manualCorrection =
        DownlinkManualCorrectionEnabled
          ? DownlinkManualCorrection
          : 0.0;

      double desiredPreDoppler =
        (targetFrequency -
         manualCorrection) /
        dopplerScale;

      if (useRit)
      {
        double nominalDownlink =
          BaseDownlinkFrequency +
          (IsTransponder
            ? TransponderOffset
            : 0.0);

        RitOffset =
          desiredPreDoppler -
          nominalDownlink;
      }
      else if (IsTransponder)
      {
        TransponderOffset =
          desiredPreDoppler -
          BaseDownlinkFrequency;
      }
      else
      {
        double desiredManualCorrection =
          targetFrequency -
          BaseDownlinkFrequency *
          dopplerScale;

        DownlinkManualCorrection =
          Math.Round(
            desiredManualCorrection,
            MidpointRounding.AwayFromZero);
      }

      return true;
    }

    private bool CanSetCorrectedDownlink(double targetFrequency, bool useRit)
    {
      if (!HasDownlink || !double.IsFinite(targetFrequency))
        return false;
      if (IsTerrestrial)
        return true;
      if (SatCust == null || Tx == null || !Tx.downlink_low.HasValue)
        return false;
      if (IsTransponder && TxCust == null)
        return false;
      return useRit || IsTransponder || DownlinkManualCorrectionEnabled;
    }

    /// <summary>
    /// Reset every operator tuning offset so the no-Doppler downlink/uplink return exactly
    /// to their saved Base frequencies.
    /// </summary>
    public void ReturnToBaseTuningPosition()
    {
      if (IsTerrestrial) return;

      RitEnabled = false;
      RitOffset = 0;
      XitOffset = 0;

      if (TxCust != null)
        TransponderOffset = 0;

      if (SatCust != null)
      {
        DownlinkManualCorrection = 0;
        UplinkManualCorrection = 0;
      }

      ComputeFrequencies();
    }

    public void IncrementUplinkFrequency(int delta)
    {
      if (IsTerrestrial)
        UplinkFrequency += delta;
      else
      {
        UplinkManualCorrection += delta;
      }

      ComputeFrequencies();
    }
  }
}
