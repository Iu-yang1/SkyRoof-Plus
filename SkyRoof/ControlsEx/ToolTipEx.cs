using System.ComponentModel;
using SkyRoof;

namespace VE3NEA
{
  // A ToolTip that follows the theme. The framework ignores BackColor/ForeColor on a plain ToolTip
  // under visual styles, but honors both in the owner-draw path, so this subclass sets the colors
  // and delegates the drawing back to the framework.
  //
  // Measured by reading the pixels back out of the tooltip's own DC (plan 4.5b): the classic Info
  // color paints as a murky #50503C in dark mode, while ControlLight gives #2E2E2E. The border
  // comes from SystemColors.WindowFrame and flips on its own.
  public class ToolTipEx : ToolTip
  {
    // Explicit text insets keep multilingual, multi-line tooltips away from the border.
    private const int HorizontalInset = 6;
    private const int VerticalInset = 5;

    internal static Size AddTextInsets(Size native) =>
      new(native.Width + HorizontalInset * 2, native.Height + VerticalInset * 2);

    public ToolTipEx() : base()
    {
      Initialize();
    }

    public ToolTipEx(IContainer cont) : base(cont)
    {
      Initialize();
    }

    private void Initialize()
    {
      OwnerDraw = true;
      BackColor = Theme.TipBack;
      ForeColor = Theme.TipText;
      Popup += (_, e) => e.ToolTipSize = AddTextInsets(e.ToolTipSize);
      Draw += DrawTooltip;
    }

    // ToolTip.OnDraw is not virtual, so the subclass subscribes to its own Draw event
    private void DrawTooltip(object? sender, DrawToolTipEventArgs e)
    {
      e.DrawBackground();
      e.DrawBorder();

      // Native DrawText can clip a multi-line tooltip. Reserve the same insets
      // in Popup sizing and Draw bounds: enlarging the rectangle vertically
      // would move its first line above the tooltip window.
      const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix;
      var rect = Rectangle.Inflate(e.Bounds, -HorizontalInset, -VerticalInset);

      // the native control reserves room for the title but paints none of it, so without this a
      // titled tooltip would show its headline as an empty strip
      if (!string.IsNullOrEmpty(ToolTipTitle))
      {
        using var titleFont = new Font(e.Font, FontStyle.Bold);
        var titleHeight = TextRenderer.MeasureText(e.Graphics, ToolTipTitle, titleFont, rect.Size, flags).Height;
        TextRenderer.DrawText(e.Graphics, ToolTipTitle, titleFont, rect, ForeColor, flags);
        rect.Y += titleHeight;
        rect.Height -= titleHeight;
      }

      TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, rect, ForeColor, flags);
    }
  }
}
