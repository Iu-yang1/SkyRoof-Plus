using System.Globalization;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly Dictionary<string, TextBox> qsoFields = new();
    private readonly DateTimePicker qsoUtc = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Width = 185 };
    private readonly TextBox qsoFilter = new() { Width = 110 };
    private readonly ListView qsoList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    private readonly Label qsoStatus = new() { Dock = DockStyle.Bottom, Height = 30 };
    private string qsoId = Guid.NewGuid().ToString("N");
    private string qsoCursor = "", currentFilter = "";
    private bool qsoBusy;

    private TabPage CreateQsoPage()
    {
        var page = new TabPage("QSO LOG");
        var fields = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        fields.Controls.Add(Row("UTC", qsoUtc, ActionButton("NOW", () => { qsoUtc.Value = DateTime.UtcNow; return Task.CompletedTask; }),
            ActionButton("USE CAT", FillQsoFromRadioAsync)));
        foreach (string[] group in new[] {
            new[] {"MY CALL", "MY GRID", "CALL", "GRID"}, new[] {"BAND", "MODE", "SUBMODE", "SAT"},
            new[] {"TX Hz", "RX Hz", "PROP", "SENT", "RCVD"}, new[] {"NAME", "STATE", "NOTES"} })
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            foreach (string name in group)
            {
                var box = new TextBox { Width = name == "NOTES" ? 220 : 95 };
                qsoFields.Add(name, box);
                row.Controls.Add(new Label { Text = name, AutoSize = true });
                row.Controls.Add(box);
            }
            fields.Controls.Add(row);
        }
        qsoUtc.Value = DateTime.UtcNow;
        fields.Controls.Add(Row(ActionButton("SAVE QSO", SaveQsoAsync), ActionButton("NEW QSO", NewQsoAsync),
            "Search call", qsoFilter, ActionButton("SEARCH LOG", () => QueryQsoAsync(true)), ActionButton("NEXT PAGE", () => QueryQsoAsync(false)),
            ActionButton("EXPORT ADIF", ExportQsoAsync)));
        foreach (var column in new[] { "UTC", "CALL", "GRID", "BAND", "MODE", "SAT", "SENT", "RCVD" }) qsoList.Columns.Add(column, 100);
        page.Controls.Add(qsoList); page.Controls.Add(fields); page.Controls.Add(qsoStatus);
        return page;
    }

    private string QsoField(string name) => qsoFields[name].Text.Trim();
    private long QsoHz(string name) => string.IsNullOrWhiteSpace(QsoField(name)) ? 0 : long.Parse(QsoField(name), CultureInfo.InvariantCulture);

    private QsoRecord BuildQso() => new()
    {
        Id = qsoId, UtcMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(qsoUtc.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
        StationCall = QsoField("MY CALL"), MyGrid = QsoField("MY GRID"), Call = QsoField("CALL"), Grid = QsoField("GRID"),
        Band = QsoField("BAND"), Mode = QsoField("MODE"), Submode = QsoField("SUBMODE"), Satellite = QsoField("SAT"),
        TxHz = QsoHz("TX Hz"), RxHz = QsoHz("RX Hz"), Propagation = QsoField("PROP"), Sent = QsoField("SENT"),
        Received = QsoField("RCVD"), Name = QsoField("NAME"), State = QsoField("STATE"), Notes = QsoField("NOTES")
    };

    private async Task FillQsoFromRadioAsync()
    {
        var radioState = await client.ReadRadioAsync(lifetime.Token);
        qsoFields["TX Hz"].Text = radioState.TxFrequencyHz.ToString(CultureInfo.InvariantCulture);
        qsoFields["RX Hz"].Text = radioState.RxFrequencyHz.ToString(CultureInfo.InvariantCulture);
        qsoFields["MODE"].Text = radioState.TxMode switch { "USB" or "LSB" => "SSB", _ => radioState.TxMode };
        if (activeTracking?.CelestialTarget == "MOON") qsoFields["PROP"].Text = "EME";
        var hz = radioState.TxFrequencyHz;
        qsoFields["BAND"].Text = hz switch { >= 144000000 and <= 148000000 => "2m", >= 420000000 and <= 450000000 => "70cm", >= 1240000000 and <= 1300000000 => "23cm", >= 2300000000 and <= 2450000000 => "13cm", _ => qsoFields["BAND"].Text };
        if (autoCall.Text.Length > 0) qsoFields["MY CALL"].Text = autoCall.Text;
        if (autoGrid.Text.Length > 0) qsoFields["MY GRID"].Text = autoGrid.Text;
    }

    private async Task SaveQsoAsync()
    {
        if (qsoBusy) return;
        qsoBusy = true;
        try
        {
            var saved = await client.SaveQsoAsync(BuildQso(), lifetime.Token);
            qsoStatus.Text = "Saved · " + saved.Id;
            recordingWriter?.MarkQso(saved.Id, saved.Call);
            await NewQsoAsync();
        }
        finally { qsoBusy = false; }
    }

    private Task NewQsoAsync()
    {
        qsoId = Guid.NewGuid().ToString("N");
        qsoUtc.Value = DateTime.UtcNow;
        foreach (var field in new[] { "CALL", "GRID", "NAME", "STATE", "NOTES", "SENT", "RCVD" }) qsoFields[field].Clear();
        return Task.CompletedTask;
    }

    private async Task QueryQsoAsync(bool reset)
    {
        if (qsoBusy) return;
        if (!reset && qsoCursor.Length == 0) return;
        qsoBusy = true;
        try
        {
            if (reset) { currentFilter = qsoFilter.Text.Trim(); qsoCursor = ""; }
            var result = await client.SearchQsoAsync(new QsoSearch { Call = currentFilter, Limit = 100, BeforeId = qsoCursor }, lifetime.Token);
            qsoList.Items.Clear();
            foreach (var q in result.Records)
                qsoList.Items.Add(new ListViewItem(new[] { UtcLabel(q.UtcMilliseconds), q.Call, q.Grid, q.Band,
                    q.Submode.Length == 0 ? q.Mode : q.Submode, q.Satellite, q.Sent, q.Received }));
            qsoCursor = result.NextBeforeId;
            qsoStatus.Text = $"{result.Records.Count} records · {(qsoCursor.Length > 0 ? "more available" : "end")}";
        }
        finally { qsoBusy = false; }
    }

    private async Task ExportQsoAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "ADIF|*.adi", FileName = "skyroof-log.adi" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        // Fetch all matching pages rather than silently truncating the export.
        string cursor = "";
        int count = 0;
        using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(false));
        do
        {
            var query = new QsoSearch { Call = qsoFilter.Text.Trim(), Limit = 100, BeforeId = cursor };
            var page = await client.SearchQsoAsync(query, lifetime.Token);
            var export = await client.ExportQsoAsync(query, lifetime.Token);
            if (export.Count != page.Records.Count) throw new IOException("Logbook changed during export; retry.");
            string content = export.Content;
            int header = content.IndexOf("<EOH>", StringComparison.OrdinalIgnoreCase);
            await writer.WriteAsync(count == 0 ? content : content[(header + 5)..]);
            count += export.Count;
            cursor = page.NextBeforeId;
        } while (cursor.Length > 0);
        qsoStatus.Text = $"Exported {count} QSO records.";
    }
}
