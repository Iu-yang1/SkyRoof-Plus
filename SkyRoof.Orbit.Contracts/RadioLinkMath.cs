namespace SkyRoof.Orbit.Contracts;

// Database passband edges describe the nominal transponder. Operators may
// tune beyond those edges; the RF hardware determines supported frequencies.
public readonly record struct SatellitePassband(long? DownlinkLowHz, long? DownlinkHighHz,
    long? UplinkLowHz, long? UplinkHighHz, bool Inverting)
{
    public bool IsTransponder => DownlinkHighHz.HasValue && DownlinkHighHz != DownlinkLowHz &&
        UplinkLowHz.HasValue && UplinkHighHz.HasValue;

    public double DownlinkNominal(long baseOffset, double position) =>
        DownlinkLowHz is long low ? low + (double)baseOffset + (IsTransponder ? position : 0) : 0;

    public double UplinkNominal(long baseOffset, double position)
    {
        if (UplinkLowHz is not long low) return 0;
        return IsTransponder && Inverting
            ? UplinkHighHz.GetValueOrDefault() + (double)baseOffset - position
            : low + (double)baseOffset + (IsTransponder ? position : 0);
    }
}

public static class RadioLinkMath
{
    public const double SpeedOfLightMetresPerSecond = 299_792_458.0;

    // The original RadioLink applies RIT before RX Doppler, manual correction
    // afterwards; TX Doppler precedes manual correction and XIT.
    public static double CorrectDownlink(double nominal, double factor, double rit, double manual) =>
        (nominal + rit) * (1 - factor) + manual;

    public static double CorrectUplink(double nominal, double factor, double xit, double manual) =>
        nominal > 0 ? nominal * (1 + factor) + manual + xit : nominal;
}
