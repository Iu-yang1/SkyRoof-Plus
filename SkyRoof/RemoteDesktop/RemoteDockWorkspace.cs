using WeifenLuo.WinFormsUI.Docking;

namespace SkyRoof.RemoteDesktop;

// Reuse the original MainForm's docking library and theme. All content
// still belongs to the same StationIpcForm/StationIpcClient lifetime.
internal sealed class RemoteDockWorkspace : UserControl
{
    private readonly DockPanel host = new() { Dock = DockStyle.Fill, ShowAutoHideContentOnHover = false,
        DocumentStyle = DocumentStyle.DockingWindow };
    private readonly MenuStrip menu = new() { Dock = DockStyle.Top };
    private readonly List<RemoteDockPane> panes = [];
    private bool opened;

    internal IReadOnlyList<RemoteDockPane> Panes => panes;

    internal RemoteDockWorkspace(IEnumerable<TabPage> pages)
    {
        Dock = DockStyle.Fill;
        host.Theme = Theme.Mode switch
        {
            ThemeMode.GitHubLight => new GitHubLightDockTheme(),
            ThemeMode.GitHubDark => new GitHubDarkDockTheme(),
            ThemeMode.Dark => new VS2015DarkTheme(),
            _ => Theme.IsDark ? new VS2015DarkTheme() : new PinkBlueLightDockTheme()
        };
        var view = new ToolStripMenuItem("VIEW");
        menu.Items.Add(view);
        foreach (var page in pages)
        {
            var pane = new RemoteDockPane(page);
            panes.Add(pane);
            var item = new ToolStripMenuItem(pane.Text);
            item.Click += (_, _) => pane.Show(host, DockState.Document);
            view.DropDownItems.Add(item);
        }
        Controls.Add(host);
        Controls.Add(menu);
    }

    internal void AddSettingsAction(Func<Task> action, Func<Func<Task>, Task> run)
    {
        var item = new ToolStripMenuItem("SETTINGS");
        item.Click += async (_, _) => await run(action);
        menu.Items.Add(item);
    }

    internal void OpenInitialLayout()
    {
        if (opened) return;
        opened = true;
        foreach (var pane in panes) pane.Show(host, DockState.Document);
        if (panes.Count > 0) panes[0].Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var pane in panes)
                if (!pane.IsDisposed) pane.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class RemoteDockPane : DockContent
{
    internal RemoteDockPane(TabPage page)
    {
        Text = page.Text;
        TabText = page.Text;
        HideOnClose = true;
        DockAreas = DockAreas.Document | DockAreas.DockLeft | DockAreas.DockRight |
            DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Float;
        foreach (var child in page.Controls.Cast<Control>().ToArray())
            Controls.Add(child);
        page.Dispose();
    }
}
