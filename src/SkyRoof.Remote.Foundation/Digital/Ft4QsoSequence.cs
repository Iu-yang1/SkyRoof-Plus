using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyRoof.Remote.Foundation.Digital;

public enum Ft4Exchange { Cq, Grid, Report, RogerReport, Rr73, Final73, Complete, Exhausted }

public sealed record Ft4DecodedContact(string To, string From, string Exchange, int Snr);

public sealed class Ft4QsoSequence
{
    private readonly string _call;
    private readonly string _grid;
    private readonly int _attemptLimit;
    private int _attempts;
    private int _snr;

    public Ft4QsoSequence(string call, string grid, string target, int attemptLimit)
    {
        _call = NormalizeCall(call);
        _grid = grid.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(_grid, "^[A-R]{2}[0-9]{2}([A-X]{2})?$") || attemptLimit < 1)
            throw new ArgumentException("Valid Maidenhead grid and positive attempt limit required.");
        _grid = _grid[..4];
        _attemptLimit = attemptLimit;
        Peer = string.IsNullOrWhiteSpace(target) ? null : NormalizeCall(target);
        if (Peer == _call) throw new ArgumentException("Cannot call the local station.");
        Stage = Peer is null ? Ft4Exchange.Cq : Ft4Exchange.Grid;
    }

    public string? Peer { get; private set; }
    public Ft4Exchange Stage { get; private set; }
    public string? PeerGrid { get; private set; }
    public int? ReportReceived { get; private set; }
    public int? ReportSent { get; private set; }
    public bool Finished => Stage is Ft4Exchange.Complete or Ft4Exchange.Exhausted;
    public string? Message => Stage switch
    {
        Ft4Exchange.Cq => $"CQ {_call} {_grid}",
        Ft4Exchange.Grid => $"{Peer} {_call} {_grid}",
        Ft4Exchange.Report => $"{Peer} {_call} {FormatReport(_snr)}",
        Ft4Exchange.RogerReport => $"{Peer} {_call} R{FormatReport(_snr)}",
        Ft4Exchange.Rr73 => $"{Peer} {_call} RR73",
        Ft4Exchange.Final73 => $"{Peer} {_call} 73",
        _ => null
    };

    public void Sent()
    {
        if (Finished) throw new InvalidOperationException("QSO already ended.");
        if (Stage is Ft4Exchange.Report or Ft4Exchange.RogerReport) ReportSent = Math.Clamp(_snr, -24, 49);
        _attempts++;
        if (Stage == Ft4Exchange.Final73) Stage = Ft4Exchange.Complete;
    }

    public void Observe(IEnumerable<Ft4DecodedContact> contacts)
    {
        if (Finished || _attempts == 0) return;
        foreach (var item in contacts.OrderByDescending(x => x.Snr).ThenBy(x => x.From, StringComparer.Ordinal))
        {
            if (item.To != _call || item.From == _call || (Peer is not null && item.From != Peer)) continue;
            if (!IsStandardCall(item.From)) continue;
            var next = Next(item.Exchange);
            if (next is null || (int)next.Value < (int)Stage) continue;
            Peer = item.From;
            if (Regex.IsMatch(item.Exchange, "^[A-R]{2}[0-9]{2}$")) PeerGrid = item.Exchange;
            if (TryReport(item.Exchange, out int report)) ReportReceived = report;
            if (next.Value != Stage) { Stage = next.Value; _attempts = 0; _snr = item.Snr; }
            return;
        }
    }

    public void FinishReceiveWindow()
    {
        if (!Finished && _attempts >= _attemptLimit) Stage = Ft4Exchange.Exhausted;
    }

    private Ft4Exchange? Next(string exchange)
    {
        if (exchange == "73" && Stage == Ft4Exchange.Rr73) return Ft4Exchange.Complete;
        if (exchange is "RR73" or "RRR" && (int)Stage >= (int)Ft4Exchange.RogerReport) return Ft4Exchange.Final73;
        if (TryReport(exchange, out _))
        {
            if (exchange.StartsWith('R')) return ReportSent.HasValue ? Ft4Exchange.Rr73 : null;
            return Ft4Exchange.RogerReport;
        }
        if (Regex.IsMatch(exchange, "^[A-R]{2}[0-9]{2}$")) return Ft4Exchange.Report;
        return null;
    }

    private static bool TryReport(string value, out int report)
    {
        report = 0;
        if (!Regex.IsMatch(value, "^R?[+-][0-9]{2}$")) return false;
        return int.TryParse(value.TrimStart('R'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out report);
    }

    private static string FormatReport(int snr) => Math.Clamp(snr, -24, 49).ToString("+00;-00;+00", CultureInfo.InvariantCulture);
    private static bool IsStandardCall(string call) => Regex.IsMatch(call, "^([A-Z]{0,2}|[A-Z][0-9]|[0-9][A-Z])[0-9][A-Z]{1,3}(/R|/P)?$");

    private static string NormalizeCall(string call)
    {
        string value = call.Trim().ToUpperInvariant();
        if (!IsStandardCall(value)) throw new ArgumentException("Automatic standard FT4 exchange requires a standard callsign.");
        return value;
    }
}
