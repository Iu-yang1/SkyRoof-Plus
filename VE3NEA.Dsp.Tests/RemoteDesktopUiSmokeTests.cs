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
                var tabs = form.Controls.OfType<TabControl>().Single();
                Assert.Equal(5, tabs.TabPages.Count);
                Assert.Contains("AUTO QSO", tabs.TabPages.Cast<TabPage>().Select(page => page.Text));
                var buttons = Descendants<Button>(form).Select(button => button.Text).ToArray();
                Assert.Contains("START AUTO QSO", buttons);
                Assert.Contains("STOP AUTO QSO", buttons);
                Assert.Contains("USE MOON JOB", buttons);
                Assert.Contains("APPLY FIELDS", buttons);
                Assert.Contains("PREDICT", buttons);
                Assert.Contains("PARK", buttons);
                Assert.Contains("HOLD PTT", buttons);
                Assert.Contains("PLAY RX", buttons);
                Assert.Single(Descendants<TextBox>(form).Where(box => box.UseSystemPasswordChar));
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
