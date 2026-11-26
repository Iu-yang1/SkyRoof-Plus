using System.Windows.Forms;
using SkyRoof.RemoteDesktop;
using Xunit;

namespace VE3NEA.Dsp.Tests;

[Collection("BilingualUiAcceptance")]
public sealed class RemoteDesktopUiSmokeTests
{
    [Fact]
    public void WorkbenchHasNativeControlsAndNoAutomaticHardwareStartupOnConstruction()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new StationIpcForm("skyroof-offline-ui-" + Guid.NewGuid().ToString("N"));
                var workspace = form.Controls.OfType<RemoteDockWorkspace>().Single();
                Assert.Equal(10, workspace.Panes.Count);
                Assert.Contains("AUTO QSO", workspace.Panes.Select(page => page.Text));
                Assert.All(workspace.Panes, pane => Assert.True(pane.HideOnClose));
                var buttons = workspace.Panes.SelectMany(Descendants<Button>).Select(button => button.Text).ToArray();
                Assert.Contains("START AUTO QSO", buttons);
                Assert.Contains("STOP AUTO QSO", buttons);
                Assert.Contains("USE MOON JOB", buttons);
                Assert.Contains("APPLY FIELDS", buttons);
                Assert.Contains("PREDICT", buttons);
                Assert.Contains("PARK", buttons);
                Assert.Contains("HOLD PTT", buttons);
                Assert.Contains("PLAY RX", buttons);
                Assert.Contains("FIND PASSES", buttons);
                Assert.Contains("GROUND TRACK", buttons);
                Assert.Contains("RESET MAP", buttons);
                Assert.Contains("IMPORT ADIF", buttons);
                Assert.Contains("SAVE QSO", buttons);
                Assert.Contains("RECORD WAV", buttons);
                Assert.Contains("START AFC", buttons);
                Assert.Contains("APPLY TARGET", buttons);
                Assert.Contains("START STATION AFC", buttons);
                Assert.Contains("STOP STATION AFC", buttons);
                Assert.Contains("START STATION RECORD", buttons);
                Assert.Contains("STOP STATION RECORD", buttons);
                Assert.Contains("GPU / online map", workspace.Panes.SelectMany(Descendants<CheckBox>).Select(box => box.Text));
                Assert.Single(workspace.Panes.SelectMany(Descendants<TextBox>).Where(box => box.UseSystemPasswordChar));
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Remote form construction timed out.");
        if (failure is not null) throw new InvalidOperationException("Remote desktop UI construction failed.", failure);
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            if (control is T typed) yield return typed;
            foreach (var child in Descendants<T>(control)) yield return child;
        }
    }
}
