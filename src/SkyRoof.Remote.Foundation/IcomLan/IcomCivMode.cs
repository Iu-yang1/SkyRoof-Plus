namespace SkyRoof.Remote.Foundation.IcomLan;

public readonly record struct IcomCivMode(byte Code, byte Filter)
{
    public static byte ParseCode(string mode) => mode.Trim().ToUpperInvariant() switch
    {
        "LSB" => 0, "USB" => 1, "AM" => 2, "CW" => 3,
        "RTTY" => 4, "FM" => 5, "CWR" or "CW-R" => 7,
        "RTTYR" or "RTTY-R" => 8, "DV" => 0x17, "DD" => 0x22,
        _ => throw new ArgumentException("Unknown IC-9700 CI-V mode.", nameof(mode))
    };
}
