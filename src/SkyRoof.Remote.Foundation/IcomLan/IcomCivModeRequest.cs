namespace SkyRoof.Remote.Foundation.IcomLan;

internal readonly record struct IcomCivModeRequest(byte Code, bool Data)
{
    public static IcomCivModeRequest Parse(string mode)
    {
        string name = mode.Trim().ToUpperInvariant();
        return name switch
        {
            "PKTUSB" or "USB-D" => new(1, true),
            "PKTLSB" or "LSB-D" => new(0, true),
            "PKTFM" or "FM-D" => new(5, true),
            "PKTAM" or "AM-D" => new(2, true),
            _ => new(IcomCivMode.ParseCode(name), false)
        };
    }
}
