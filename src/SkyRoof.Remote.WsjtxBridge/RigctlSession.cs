using System.Globalization;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.WsjtxBridge;

public sealed partial class RigctlSession(IRemoteRig rig, DigitalPttSession ptt)
{
    private bool _split;
    private string _vfo = "VFOA";
    private string _txVfo = "VFOB";

    public async Task<string> ExecuteAsync(string line, CancellationToken ct)
    {
        var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (args.Length == 0) return "RPRT -1";
        string command = Normalize(args[0]);
        try
        {
            return command switch
            {
                "chk_vfo" => "0",
                "dump_state" => DumpState(await rig.ReadAsync(ct)),
                "get_info" => "SkyRoof Remote WSJT-X Bridge",
                "f" => Number((await rig.ReadAsync(ct)).RxFrequencyHz),
                "i" => Number((await rig.ReadAsync(ct)).TxFrequencyHz),
                "m" => ModeReply((await rig.ReadAsync(ct)).RxMode),
                "x" => ModeReply((await rig.ReadAsync(ct)).TxMode),
                "t" => (await rig.ReadAsync(ct)).ActualPtt ? "1" : "0",
                "F" => await FrequencyAsync(args, false, ct),
                "I" => await FrequencyAsync(args, true, ct),
                "M" => await ModeAsync(args, false, ct),
                "X" => await ModeAsync(args, true, ct),
                "T" => await PttAsync(args, ct),
                "v" => _vfo,
                "V" => SetVfo(args),
                "s" => (_split ? "1" : "0") + "\n" + _txVfo,
                "S" => await SplitAsync(args, ct),
                _ => "RPRT -4"
            };
        }
        catch (ArgumentException) { return "RPRT -1"; }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Console.Error.WriteLine("CAT " + command + ": " + e.Message);
            return "RPRT -5";
        }
    }

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
        var state = await rig.ExecuteAsync(new RigCommand("", tx ? DeviceOperation.DeviceTxMode :
            DeviceOperation.DeviceRxMode, Mode: mode), ct);
        return string.Equals(tx ? state.TxMode : state.RxMode, mode, StringComparison.OrdinalIgnoreCase)
            ? "RPRT 0" : "RPRT -5";
    }

    private async Task<string> PttAsync(string[] args, CancellationToken ct)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out int value) || value is < 0 or > 3)
            return "RPRT -1";
        if (value == 0) return await ptt.SetAsync(false, 0, "", ct) ? "RPRT 0" : "RPRT -5";
        var state = await rig.ReadAsync(ct);
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
