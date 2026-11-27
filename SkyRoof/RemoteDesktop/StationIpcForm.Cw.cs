using System.Globalization;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed partial class StationIpcForm
{
    private readonly Label cwStatus = new() { AutoSize = true, Text = "CW · read status" };
    private readonly TextBox cwWpm = new() { Width = 60, Text = "20" };
    private readonly TextBox cwMessage = new() { Width = 230 };
    private CancellationTokenSource? cwSendCancellation;

    private Control CreateCwRow()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        panel.Controls.Add(Row(cwStatus, ActionButton("CW STATUS", ReadCwAsync),
            "WPM", cwWpm, ActionButton("SET WPM", SetCwSpeedAsync)));
        panel.Controls.Add(Row(cwMessage, ActionButton("SEND CW", SendCwAsync), ActionButton("STOP CW", StopCwAsync)));
        return panel;
    }

    private async Task ReadCwAsync() => ShowCw(await client.ReadCwAsync(lifetime.Token));

    private async Task SetCwSpeedAsync()
    {
        var wpm = double.Parse(cwWpm.Text, CultureInfo.InvariantCulture);
        ShowCw(await client.SetCwSpeedAsync(wpm, lifetime.Token));
    }

    private async Task SendCwAsync()
    {
        if (cwSendCancellation is not null || pttClosing) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cwSendCancellation = cancellation;
        cwStatus.Text = "CW · sending";
        try
        {
            var receipt = await client.SendCwAsync(cwMessage.Text, cancellation.Token);
            if (!IsDisposed) cwStatus.Text = "CW · " + receipt.State;
        }
        finally { cwSendCancellation = null; }
    }

    private async Task StopCwAsync()
    {
        if (cwSendCancellation is null) return;
        cwSendCancellation.Cancel();
        var snapshot = await client.ReleaseSessionAsync(CancellationToken.None);
        ShowRadio(snapshot);
        if (!IsDisposed) cwStatus.Text = "CW · " + snapshot.StatusText;
    }

    private void ShowCw(CwRadioStatus snapshot)
    {
        if (IsDisposed) return;
        cwStatus.Text = $"CW · {snapshot.Lease} · {snapshot.Mode} · BK-IN {snapshot.BreakIn} · {snapshot.Wpm:F1} WPM";
        cwWpm.Text = snapshot.Wpm.ToString("F1", CultureInfo.InvariantCulture);
    }
}
