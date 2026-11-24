using System.Globalization;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.WsjtxBridge;

public sealed partial class RigctlSession
{
    private Task<RigStatus> ReadModesAsync(CancellationToken ct) =>
        rig.ExecuteAsync(new RigCommand("", DeviceOperation.DeviceReadModes), ct);

    private static string Number(long hz) => hz.ToString(CultureInfo.InvariantCulture);

    private async Task<string> FrequencyAsync(string[] args, bool tx, CancellationToken ct)
    {
        if (args.Length != 2 || !double.TryParse(args[1], NumberStyles.Float,
            CultureInfo.InvariantCulture, out double hz) || !double.IsFinite(hz) || hz <= 0 || hz > long.MaxValue)
            return "RPRT -1";
        long target = checked((long)Math.Round(hz));
        var state = await rig.ExecuteAsync(new RigCommand("", tx ? DeviceOperation.DeviceTxFrequency :
            DeviceOperation.DeviceRxFrequency, target), ct);
        return (tx ? state.TxFrequencyHz : state.RxFrequencyHz) == target ? "RPRT 0" : "RPRT -5";
    }

    private async Task<string> ModeAsync(string[] args, bool tx, CancellationToken ct)
    {
        if (args.Length != 3) return "RPRT -1";
        string mode = RadioMode(args[1]);
        if (mode.Length == 0) return "RPRT -4";
        await rig.ExecuteAsync(new RigCommand("", tx ? DeviceOperation.DeviceTxMode :
            DeviceOperation.DeviceRxMode, Mode: mode), ct);
        var state = await ReadModesAsync(ct);
        return string.Equals(tx ? state.TxMode : state.RxMode, mode, StringComparison.OrdinalIgnoreCase)
            ? "RPRT 0" : "RPRT -5";
    }

    private async Task<string> PttAsync(string[] args, CancellationToken ct)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out int value) || value is < 0 or > 3)
            return "RPRT -1";
        if (value == 0) return await ptt.SetAsync(false, 0, "", ct) ? "RPRT 0" : "RPRT -5";
        var state = await ReadModesAsync(ct);
        bool duplex = _split || (state.SupportsDuplexFrequencies && state.RxFrequencyHz != state.TxFrequencyHz);
        long frequency = duplex ? state.TxFrequencyHz : state.RxFrequencyHz;
        string mode = duplex ? state.TxMode : state.RxMode;
        return await ptt.SetAsync(true, frequency, mode, ct) ? "RPRT 0" : "RPRT -9";
    }

    private string SetVfo(string[] args)
    {
        if (args.Length != 2 || args[1] is not ("VFOA" or "Main" or "MAIN" or "CURR")) return "RPRT -4";
        _vfo = "VFOA";
        return "RPRT 0";
    }

    private async Task<string> SplitAsync(string[] args, CancellationToken ct)
    {
        if (args.Length != 3 || args[1] is not ("0" or "1")) return "RPRT -1";
        if (args[1] == "1" && !(await rig.ReadAsync(ct)).SupportsDuplexFrequencies) return "RPRT -4";
        _split = args[1] == "1";
        _txVfo = _split ? "VFOB" : "VFOA";
        return "RPRT 0";
    }
}
