namespace SkyRoof
{
  internal sealed class IcomScopeDiagnosticsDialog : Form
  {
    private readonly IcomScopeDiagnosticsReport report;
    private readonly TextBox content = new();
    private readonly Button expand = new();
    private bool expanded;

    internal IcomScopeDiagnosticsDialog(IcomScopeDiagnosticsReport report)
    {
      this.report = report;
      var colors = ScopeUiColors.Current;
      Text = UiLocalizationService.Get("Spectrum.DiagnosticsTitle");
      StartPosition = FormStartPosition.CenterParent;
      ShowInTaskbar = false;
      MinimizeBox = false;
      AutoScaleMode = AutoScaleMode.Dpi;
      ClientSize = new Size(620, 390);
      MinimumSize = new Size(460, 300);
      BackColor = colors.Background;
      ForeColor = colors.Text;

      var layout = new TableLayoutPanel
      {
        Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12)
      };
      layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
      layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      var heading = new Label
      {
        AutoSize = true, Text = UiLocalizationService.Get("Spectrum.DiagnosticsSummary"),
        Margin = new Padding(0, 0, 0, 8)
      };
      content.Dock = DockStyle.Fill;
      content.Multiline = true;
      content.ReadOnly = true;
      content.WordWrap = true;
      content.ScrollBars = ScrollBars.Vertical;
      content.BackColor = colors.Surface;
      content.ForeColor = colors.Text;
      content.Text = report.Summary;

      var actions = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
        FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0)
      };
      var close = MakeButton(UiLocalizationService.Get("Common.Close"), colors);
      close.DialogResult = DialogResult.OK;
      CancelButton = close;
      var copy = MakeButton(UiLocalizationService.Get("Spectrum.DiagnosticsCopyAll"), colors);
      copy.Click += (_, _) => CopyAll();
      ConfigureButton(expand, colors);
      expand.Text = UiLocalizationService.Get("Spectrum.DiagnosticsExpand");
      expand.Click += (_, _) => ToggleDetails(heading);
      actions.Controls.Add(close);
      actions.Controls.Add(copy);
      actions.Controls.Add(expand);
      layout.Controls.Add(heading, 0, 0);
      layout.Controls.Add(content, 0, 1);
      layout.Controls.Add(actions, 0, 2);
      Controls.Add(layout);
    }

    private static Button MakeButton(string text, ScopeUiColors colors)
    {
      var button = new Button { Text = text };
      ConfigureButton(button, colors);
      return button;
    }

    private static void ConfigureButton(Button button, ScopeUiColors colors)
    {
      button.AutoSize = true;
      button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
      button.MinimumSize = new Size(76, 28);
      button.FlatStyle = FlatStyle.Flat;
      button.FlatAppearance.BorderColor = colors.Border;
      button.BackColor = colors.Surface;
      button.ForeColor = colors.Text;
    }

    private void ToggleDetails(Label heading)
    {
      expanded = !expanded;
      content.Text = expanded ? report.FullText : report.Summary;
      content.SelectionStart = 0;
      content.ScrollToCaret();
      heading.Text = UiLocalizationService.Get(expanded
        ? "Spectrum.DiagnosticsDetails" : "Spectrum.DiagnosticsSummary");
      expand.Text = UiLocalizationService.Get(expanded
        ? "Spectrum.DiagnosticsCollapse" : "Spectrum.DiagnosticsExpand");
    }

    private void CopyAll()
    {
      try { Clipboard.SetText(report.FullText); }
      catch (System.Runtime.InteropServices.ExternalException)
      {
        MessageBox.Show(this, UiLocalizationService.Get("Spectrum.ClipboardBusy"), Text,
          MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }
  }
}
