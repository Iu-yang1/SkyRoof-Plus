using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkyRoof.RemoteDesktop;

internal sealed class StationConfigurationEditor : Form
{
    private readonly string path;
    private readonly string original;
    private readonly JsonObject document;
    private readonly ComboBox process = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox key = new() { Width = 410, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox value = new() { Width = 510 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(650, 0) };
    private readonly CheckBox reveal = new() { Text = "Reveal value", AutoSize = true };

    internal StationConfigurationEditor(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) throw new InvalidDataException("Select an installed supervisor JSON.");
        path = Path.GetFullPath(file);
        original = File.ReadAllText(path);
        document = JsonNode.Parse(original)?.AsObject() ?? throw new InvalidDataException("Invalid supervisor JSON.");
        var processes = document["Processes"]?.AsArray() ?? throw new InvalidDataException("Missing Processes.");
        var names = processes.Select(p => p?["Name"]?.GetValue<string>() ?? "").ToArray();
        if (names.Length != 5 || !names.ToHashSet().SetEquals(new[] { "Device", "Station", "Media", "Dsp", "Host" }))
            throw new InvalidDataException("Expected installed five-child supervisor configuration.");
        Text = "Station service configuration";
        ClientSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterParent;
        process.Items.AddRange(names);
        process.SelectedIndexChanged += (_, _) => ShowKeys();
        key.SelectedIndexChanged += (_, _) => ShowValue();
        reveal.CheckedChanged += (_, _) => value.UseSystemPasswordChar = !reveal.Checked;
        value.UseSystemPasswordChar = true;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        layout.Controls.Add(Row("Process", process));
        layout.Controls.Add(Row("Environment key", key));
        layout.Controls.Add(Row("Value", value, reveal));
        layout.Controls.Add(Row(Button("LOAD VALUE", ShowValue), Button("SET VALUE", SetValue), Button("REMOVE KEY", RemoveKey)));
        layout.Controls.Add(Row(Button("AUTHORIZE THIS WINDOWS USER", AuthorizeDesktop)));;
        layout.Controls.Add(Row(Button("SAVE CONFIG", Save), Button("CANCEL", Close)));
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(650, 0),
            Text = "Edit existing or add supported SKYROOF_* / ASPNETCORE_* configuration keys. Values are masked until explicitly revealed. Changes apply after service restart. The service account's protected configuration directory requires administrator write permission. Executable and arguments remain unchanged." });
        layout.Controls.Add(status);
        Controls.Add(layout);
        process.SelectedIndex = 0;
    }

    private JsonObject EnvironmentValues() => StationConfigurationFields.ReadEnvironment(document, process.Text);

    private void AuthorizeDesktop()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Windows user SID unavailable.");
        StationConfigurationFields.AddDesktopSid(document, sid);
        process.SelectedItem = "Station";
        ShowKeys();
        key.Text = "SKYROOF_STATION_PIPE_CLIENT_SIDS";
        ShowValue();
        status.Text = "Current Windows SID staged for complete Station IPC access. Save and restart the service.";
    }

    private void ShowKeys()
    {
        key.Items.Clear();
        key.Text = "";
        value.Clear();
        key.Items.AddRange(EnvironmentValues().Select(p => p.Key).Concat(StationConfigurationFields.ForProcess(process.Text)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Cast<object>().ToArray());
        if (key.Items.Count > 0) key.SelectedIndex = 0;
    }

    private void ShowValue()
    {
        value.Text = EnvironmentValues()[key.Text]?.ToString() ?? "";
        reveal.Checked = false;
    }

    private string KeyName()
    {
        string name = key.Text.Trim();
        if (!(name.StartsWith("SKYROOF_", StringComparison.Ordinal) || name.StartsWith("ASPNETCORE_", StringComparison.Ordinal)) ||
            !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new InvalidDataException("Use a supported SKYROOF_ or ASPNETCORE_ environment key.");
        return name;
    }

    private void SetValue()
    {
        string name = KeyName();
        EnvironmentValues()[name] = value.Text;
        ShowKeys();
        key.Text = name;
        ShowValue();
        status.Text = "Value staged; save configuration to commit.";
    }

    private void RemoveKey()
    {
        EnvironmentValues().Remove(KeyName());
        ShowKeys();
        status.Text = "Key removed from staged configuration.";
    }

    private void Save()
    {
        if (!string.Equals(File.ReadAllText(path), original, StringComparison.Ordinal))
            throw new IOException("Configuration changed on disk. Reopen to avoid overwriting another edit.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, document, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(true);
            }
            File.Replace(temporary, path, null);
            DialogResult = DialogResult.OK;
            Close();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FlowLayoutPanel Row(params object[] items)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        foreach (var item in items) row.Controls.Add(item is Control control ? control : new Label { Text = item.ToString(), AutoSize = true });
        return row;
    }

    private Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception error) { status.Text = error.Message; }
        };
        return button;
    }
}
