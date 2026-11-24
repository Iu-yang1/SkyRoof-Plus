using System.Globalization;
using System.Net.Sockets;

public sealed partial class FakeRigctld
{
    private TcpClient? _cwOwner;
    private int _keyRaw = 85;

    private string? ExecuteCw(string cmd, TcpClient client)
    {
        double wpm = 6 + _keyRaw * 42.0 / 255;
        if (cmd == "STATUS")
            return $"STATUS {(_cwOwner is null ? "IDLE" : "BUSY")} MODE={_txMode} BKIN=1 TX={(_ptt || _cwOwner is not null ? 1 : 0)} KEYRAW={_keyRaw} TXHZ={_tx}";
        if (cmd.StartsWith("SETWPM ", StringComparison.Ordinal))
        {
            if (_cwOwner is not null) return "ERR BUSY";
            double requested = double.Parse(cmd.AsSpan(7), CultureInfo.InvariantCulture);
            _keyRaw = (int)Math.Round((requested - 6) * 255 / 42, MidpointRounding.AwayFromZero);
            wpm = 6 + _keyRaw * 42.0 / 255;
            return FormattableString.Invariant($"OK KEYRAW={_keyRaw} WPM={wpm:F2}");
        }
        if (cmd.StartsWith("SEND ", StringComparison.Ordinal))
        {
            if (_cwOwner is not null || _ptt) return "ERR BUSY";
            if (_txMode is not ("CW" or "CW-R")) return "ERR MODE";
            _cwOwner = client;
            return "OK";
        }
        if (cmd == "STOP")
        {
            if (!ReferenceEquals(_cwOwner, client)) return "ERR BUSY";
            _cwOwner = null;
            return "OK";
        }
        return null;
    }
}
