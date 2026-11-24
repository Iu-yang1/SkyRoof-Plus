using SkyRoof.Remote.Ipc;

namespace SkyRoof.Remote.WsjtxBridge;

public sealed partial class RigctlSession
{
    private readonly IRemoteRig rig;
    private readonly DigitalPttSession ptt;

    public RigctlSession(IRemoteRig rig, DigitalPttSession ptt)
    {
        this.rig = rig;
        this.ptt = ptt;
    }
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
                "m" => ModeReply((await ReadModesAsync(ct)).RxMode),
                "x" => ModeReply((await ReadModesAsync(ct)).TxMode),
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
}
