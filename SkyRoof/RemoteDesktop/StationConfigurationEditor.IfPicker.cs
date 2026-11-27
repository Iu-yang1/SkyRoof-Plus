using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationConfigurationEditor
{
    private Button CreateIfPickerButton()
    {
        var button = new Button { Text = "SELECT STATION IF DEVICE", AutoSize = true };
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { await SelectIfAsync(); }
            catch (Exception error) when (error is Grpc.Core.RpcException or IOException or OperationCanceledException or InvalidOperationException)
            { if (!IsDisposed) status.Text = error.Message; }
            finally { if (!button.IsDisposed) button.Enabled = true; }
        };
        return button;
    }

    private sealed record EndpointChoice(AfcEndpoint Endpoint)
    {
        public override string ToString() => $"{Endpoint.Name} · {(Endpoint.Loopback ? "loopback" : "capture")} · {Endpoint.SampleRate} Hz / {Endpoint.Channels} ch";
    }

    private async Task SelectIfAsync()
    {
        using var client = new StationIpcClient(stationPipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var reply = await client.StationIfEndpointsAsync(timeout.Token);
        if (IsDisposed) return;
        if (reply.Endpoints.Count == 0)
        {
            status.Text = "No active IF audio endpoints visible to the Station service account. Configure audio on the station or enter an endpoint ID manually.";
            return;
        }
        using var picker = new Form { Text = "Station IF audio endpoint", ClientSize = new Size(710, 200), StartPosition = FormStartPosition.CenterParent };
        var endpoints = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 660 };
        endpoints.Items.AddRange(reply.Endpoints.Select(x => new EndpointChoice(x)).Cast<object>().ToArray());
        endpoints.SelectedIndex = 0;
        var right = new CheckBox { Text = "Right IF channel", AutoSize = true };
        var detail = new Label { AutoSize = true, MaximumSize = new Size(670, 0) };
        void ShowEndpoint()
        {
            if (endpoints.SelectedItem is not EndpointChoice choice) return;
            detail.Text = choice.Endpoint.Detail + ". Confirm this endpoint carries real 12 kHz IF, not demodulated AF.";
            right.Enabled = choice.Endpoint.Channels >= 2;
            if (!right.Enabled) right.Checked = false;
        }
        endpoints.SelectedIndexChanged += (_, _) => ShowEndpoint();
        ShowEndpoint();
        var accept = new Button { Text = "SELECT", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCEL", AutoSize = true, DialogResult = DialogResult.Cancel };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        layout.Controls.Add(endpoints);
        layout.Controls.Add(right);
        layout.Controls.Add(detail);
        layout.Controls.Add(Row(accept, cancel));
        picker.Controls.Add(layout);
        picker.AcceptButton = accept; picker.CancelButton = cancel;
        if (picker.ShowDialog(this) != DialogResult.OK || endpoints.SelectedItem is not EndpointChoice selected) return;
        var environment = StationConfigurationFields.ReadEnvironment(document, "Station");
        environment["SKYROOF_AFC_IF_DEVICE"] = selected.Endpoint.Id;
        environment["SKYROOF_AFC_IF_LOOPBACK"] = selected.Endpoint.Loopback ? "true" : "false";
        environment["SKYROOF_AFC_IF_CHANNEL"] = right.Checked ? "right" : "left";
        process.SelectedItem = "Station";
        ShowKeys();
        key.Text = "SKYROOF_AFC_IF_DEVICE";
        ShowValue();
        status.Text = "Station IF endpoint staged. Save configuration and restart the service.";
    }
}
