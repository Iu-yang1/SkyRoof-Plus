namespace SkyRoof
{
  internal sealed class PttKeyCaptureForm : Form
  {
    private Keys? CapturedKey;

    private PttKeyCaptureForm(Keys current)
    {
      Text = UiLocalizationService.Get("PttKey.Title");
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      MaximizeBox = false;
      MinimizeBox = false;
      ShowInTaskbar = false;
      KeyPreview = true;
      ClientSize = new Size(430, 145);

      var label = new Label
      {
        AutoSize = false,
        Location = new Point(18, 18),
        Size = new Size(394, 70),
        TextAlign = ContentAlignment.MiddleCenter,
        Text = UiLocalizationService.Format("PttKey.Instructions",
          current == Keys.None ? UiLocalizationService.Get("PttKey.None") : current.ToString())
      };

      var cancel = new Button
      {
        Text = UiLocalizationService.Get("Common.Cancel"),
        DialogResult = DialogResult.Cancel,
        Location = new Point(325, 102),
        Size = new Size(87, 28)
      };

      Controls.Add(label);
      Controls.Add(cancel);
      CancelButton = cancel;

      KeyDown += (_, e) =>
      {
        if (e.KeyCode == Keys.Escape)
        {
          DialogResult = DialogResult.Cancel;
          Close();
          return;
        }

        if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
          return;

        CapturedKey = e.KeyCode;
        e.Handled = true;
        e.SuppressKeyPress = true;
        DialogResult = DialogResult.OK;
        Close();
      };
    }

    internal static Keys? CaptureKey(
      IWin32Window owner,
      Keys current)
    {
      using var form = new PttKeyCaptureForm(current);
      return form.ShowDialog(owner) == DialogResult.OK
        ? form.CapturedKey
        : null;
    }
  }
}
