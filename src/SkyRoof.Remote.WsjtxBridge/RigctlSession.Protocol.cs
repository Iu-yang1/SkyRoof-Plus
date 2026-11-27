namespace SkyRoof.Remote.WsjtxBridge;

public sealed partial class RigctlSession
{
    private static string Normalize(string name) => name.TrimStart('\\') switch
    {
        "get_freq" => "f", "set_freq" => "F",
        "get_split_freq" => "i", "set_split_freq" => "I",
        "get_mode" => "m", "set_mode" => "M",
        "get_split_mode" => "x", "set_split_mode" => "X",
        "get_ptt" => "t", "set_ptt" => "T",
        "get_vfo" => "v", "set_vfo" => "V",
        "get_split_vfo" => "s", "set_split_vfo" => "S",
        var value => value
    };

    private static string RadioMode(string mode) => mode.ToUpperInvariant() switch
    {
        "PKTUSB" => "USB-D", "PKTLSB" => "LSB-D", "PKTFM" => "FM-D",
        "USB" or "LSB" or "FM" or "AM" or "CW" or "CWR" => mode.ToUpperInvariant(),
        _ => ""
    };

    private static string ModeReply(string mode) => mode.ToUpperInvariant() switch
    {
        "USB-D" or "PKTUSB" => "PKTUSB\n0",
        "LSB-D" or "PKTLSB" => "PKTLSB\n0",
        "FM-D" or "PKTFM" => "PKTFM\n0",
        "USB" or "LSB" or "FM" or "AM" or "CW" or "CWR" => mode.ToUpperInvariant() + "\n0",
        _ => "RPRT -4"
    };

    // NET rigctl protocol 1 capability handshake. RF validity remains the
    // physical radio backend's responsibility; this is a transport range.
    private static string DumpState(RigStatus state)
    {
        string vfos = state.SupportsDuplexFrequencies ? "0x3" : "0x1";
        return string.Join("\n", new[]
        {
            "1", "2", "0",
            $"1.000000 100000000000.000000 0x1caf -1 -1 {vfos} 0x1", "0 0 0 0 0 0 0",
            $"1.000000 100000000000.000000 0x1caf -1 -1 {vfos} 0x1", "0 0 0 0 0 0 0",
            "0x1caf 1", "0 0", "0x1caf 0", "0 0",
            "0", "0", "0", "0", "0", "0",
            "0", "0", "0", "0", "0", "0",
            "ptt_type=1", "has_set_vfo=1", "has_get_vfo=1",
            "has_set_freq=1", "has_get_freq=1", "targetable_vfo=0",
            "rig_model=SkyRoof Remote", "done"
        });
    }
}
