using System.Data;
using WeifenLuo.WinFormsUI.Docking;
using VE3NEA;
using VE3NEA.Clock;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static SkyRoof.GroupViewPanel;
using Serilog;

namespace SkyRoof
{
  public partial class PassesPanel : DockContent
  {
    private readonly Context ctx;
    private List<ListViewItem> Items = new();
    // the row whose tooltip text has been composed, see listViewEx1_MouseMove
    private ListViewItem? HoveredItem;

    private readonly Font BoldFont;
    private readonly Pen PathPen = new Pen(Theme.Now, 2);
    private int rowHeight;
    private CancellationTokenSource? miniPathCancellation;


    public PassesPanel()
    {
      InitializeComponent();
    }

    public PassesPanel(Context ctx)
    {
      Log.Information("Creating PassesPanel");
      this.ctx = ctx;
      InitializeComponent();

      ctx.PassesPanel = this;
      ctx.MainForm.SatellitePassesMNU.Checked = true;

      BoldFont = new Font(listViewEx1.Font, FontStyle.Bold);

      rowHeight = TextRenderer.MeasureText("0", Font, Size, TextFormatFlags.NoPadding).Height * 2 + 15;
      listViewEx1.SetRowHeight(rowHeight);
      
      
      listViewEx1.SetTooltipDelay(1500);
      SetRadioButtonIndex(ctx.Settings.Ui.SatellitePassesPanel.RadioButtoIndex);
      if (listViewEx1.VirtualListSize == 0) ShowPasses();
    }




    //----------------------------------------------------------------------------------------------
    //                                        events
    //----------------------------------------------------------------------------------------------
    private void PassesPanel_FormClosing(object sender, FormClosingEventArgs e)
    {
      Log.Information("Closing PassesPanel");
      ctx.PassesPanel = null;
      ctx.MainForm.SatellitePassesMNU.Checked = false;
      miniPathCancellation?.Cancel();
    }

    private void listViewEx1_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
      e.Item = e.ItemIndex >= 0 && e.ItemIndex < Items.Count
        ? Items[e.ItemIndex] : new ListViewItem();
    }

    private void listViewEx1_Resize(object sender, EventArgs e)
    {
      listViewEx1.Columns[0].Width = listViewEx1.ClientSize.Width;
    }

    private void radioButton_CheckedChanged(object sender, EventArgs e)
    {
      var radioBtn = (RadioButton)sender;
      if (radioBtn.Checked)
      {
        ctx.Settings.Ui.SatellitePassesPanel.RadioButtoIndex = GetRadioButtonIndex();
        if (listViewEx1.VirtualListSize > 0) listViewEx1.EnsureVisible(0);
        ShowPasses();
      }
    }

    private void listViewEx1_MouseDown(object sender, MouseEventArgs e)
    {
      var item = listViewEx1.GetItemAt(e.X, e.Y);
     
      if (e.Button == MouseButtons.Left && item?.Tag is SatellitePass pass)
      {
        var sat = pass.Satellite;
        ctx.SatelliteSelector.SetSelectedSatellite(sat);
        ctx.SatelliteSelector.SetSelectedPass(pass);
      }
    }



    //----------------------------------------------------------------------------------------------
    //                                      items  
    //----------------------------------------------------------------------------------------------
    internal void ShowPasses()
    {
      IEnumerable<SatellitePass> passes = ctx.GroupPasses.Passes;
      var startTime = DateTime.UtcNow;
      var endTime = startTime + TimeSpan.FromDays(2);

      if (CurrentSatBtn.Checked)
      {
        var sat = ctx.SatelliteSelector.SelectedSatellite;

        // sat in group, show 3-day prediction
        if (!ctx.SatelliteSelector.GroupSatellites.Contains(sat))
          passes = ctx.HamPasses.Passes;
        // else show 2-hour prediction
        passes = passes.Where(p => p.Satellite == sat);
      }

      else if (GroupBtn.Checked)
      {
      }

      else
      {
        passes = ctx.HamPasses.Passes;
        endTime = startTime + TimeSpan.FromHours(2);
      }

      var nextPasses = passes
        .Where(pass => pass.EndTime >= startTime && pass.StartTime < endTime)
        .OrderBy(pass => pass.StartTime)
        .ToArray();

      // A cache hit and a minute tick can request the same passes again.
      // Preserving native virtual-list items/scroll state prevents a full
      // erase/repaint and avoids rescheduling already-computed mini paths.
      if (SamePassSequence(Items, nextPasses)) return;

      miniPathCancellation?.Cancel();
      Items = CreatePassItems(nextPasses).ToList();
      if (listViewEx1.VirtualListSize != Items.Count)
        listViewEx1.VirtualListSize = Items.Count;
      listViewEx1.Invalidate();

      var missingPaths = nextPasses
        .Where(pass => Volatile.Read(ref pass.MiniPath) == null).ToArray();
      if (missingPaths.Length != 0)
        _ = PrepareMiniPathsAsync(missingPaths);
    }

    internal static bool SamePassSequence<T>(
      IReadOnlyList<ListViewItem> displayed, IReadOnlyList<T> next) where T : class
    {
      return displayed.Count == next.Count &&
        displayed.Zip(next, (row, pass) => ReferenceEquals(row.Tag, pass))
          .All(same => same);
    }

    private async Task PrepareMiniPathsAsync(SatellitePass[] passes)
    {
      var cancellation = new CancellationTokenSource();
      miniPathCancellation = cancellation;
      try
      {
        await Task.Run(() =>
        {
          foreach (var pass in passes)
          {
            cancellation.Token.ThrowIfCancellationRequested();
            pass.MakeMiniPath();
          }
        }, cancellation.Token);
        if (!IsDisposed && !Disposing && IsHandleCreated &&
            ReferenceEquals(miniPathCancellation, cancellation))
          listViewEx1.Invalidate();
      }
      catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
      catch (Exception ex) { Log.Warning(ex, "Mini pass path preparation failed."); }
      finally
      {
        if (ReferenceEquals(miniPathCancellation, cancellation))
          miniPathCancellation = null;
        cancellation.Dispose();
      }
    }

    internal void UpdatePassTimes()
    {
      // Refresh countdown labels without resetting the virtual list every second.
      // Assigning VirtualListSize unconditionally tears down the native list-view
      // display cache and produces periodic full-white repaints.
      var now = DateTime.UtcNow;
      for (int i = Items.Count - 1; i >= 0; i--)
        if (Items[i].Tag is SatellitePass pass && pass.EndTime < now)
          Items.RemoveAt(i);

      if (listViewEx1.VirtualListSize != Items.Count)
        listViewEx1.VirtualListSize = Items.Count;

      if (Items.Count > 0 && listViewEx1.IsHandleCreated)
      {
        // Only invalidate rows on-screen. Full-range redraw invalidates the
        // virtual list's entire background once a second while scrolling.
        int topIndex = listViewEx1.TopItem?.Index ?? 0;
        var (first, last) = VisibleRedrawRange(
          Items.Count, topIndex, listViewEx1.ClientSize.Height, rowHeight);
        listViewEx1.RedrawItems(first, last, invalidateOnly: true);
      }
    }

    internal static (int First, int Last) VisibleRedrawRange(
      int count, int topIndex, int viewHeight, int itemHeight)
    {
      int first = Math.Clamp(topIndex, 0, count - 1);
      int h = Math.Max(itemHeight, 1);
      int visibleCount = Math.Max(1, Math.Max(0, viewHeight) / h + 2);
      return (first, Math.Min(count - 1, first + visibleCount - 1));
    }

    public IEnumerable<ListViewItem> CreatePassItems(IEnumerable<SatellitePass> passes)
    {
      // wrap passes in listview items
      return passes.OrderBy(p => p.StartTime).Select(ItemForPass);
    }

    private ListViewItem ItemForPass(SatellitePass pass)
    {
      var item = new ListViewItem();
      item.Tag = pass;

      return item;
    }

    private int GetRadioButtonIndex()
    {
      if (CurrentSatBtn.Checked) return 0;
      if (GroupBtn.Checked) return 1;
      return 2;
    }

    private void SetRadioButtonIndex(int index)
    {
      CurrentSatBtn.Checked = index == 0;
      GroupBtn.Checked = index == 1;
      AllBtn.Checked = index == 2;
    }




    //----------------------------------------------------------------------------------------------
    //                                        draw
    //----------------------------------------------------------------------------------------------
    private void listViewEx1_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
      e.DrawBackground();
      if (e.Item.Tag is not SatellitePass pass) return;
      int h = e.Bounds.Height - 1;
      int w = e.Bounds.Width - h;
      bool now = pass.StartTime < DateTime.UtcNow;

      // sat name
      string text = pass.Satellite.name;
      var size = e.Graphics.MeasureString(text, BoldFont);
      var rect = new RectangleF(e.Bounds.X, e.Bounds.Y, size.Width, size.Height);
      Brush brush = SystemBrushes.Window;
      if (pass.Satellite.Flags.HasFlag(SatelliteFlags.Uhf)) brush = Theme.UhfTintBrush;
      else if (pass.Satellite.Flags.HasFlag(SatelliteFlags.Vhf)) brush = Theme.VhfTintBrush;
      e.Graphics.FillRectangle(brush, rect);
      e.Graphics.DrawString(text, BoldFont, Theme.RowTextBrush(false), rect);

      // TLE satellites have a real revolution number. JPL ephemeris
      // targets use an internal pass key only; do not expose it as an orbit.
      text = pass.HasOrbitNumber ? $"#{pass.OrbitNumber}" : "JPL";
      size = e.Graphics.MeasureString(text, listViewEx1.Font);
      rect = new RectangleF(rect.Right + 10, rect.Y, size.Width, size.Height);
      e.Graphics.DrawString(text, listViewEx1.Font, SystemBrushes.WindowText, rect);

      // start/end time
      text = pass.Geostationary ? "Geostationary" :
        $"{ClockWidget.FromUtc(pass.StartTime):yyyy-MM-dd  HH:mm:ss}  to  " +
        $"{ClockWidget.FromUtc(pass.EndTime):HH:mm:ss} {ClockWidget.Suffix}";
      size = e.Graphics.MeasureString(text, listViewEx1.Font);
      rect = new RectangleF(e.Bounds.X, e.Bounds.Y + e.Bounds.Height - size.Height - 2, size.Width, size.Height);
      e.Graphics.DrawString(text, listViewEx1.Font, SystemBrushes.WindowText, rect);

      // duration / elevation
      text = pass.Geostationary ?
        $"{pass.MaxElevation:F0}°" : $"{Utils.TimespanToString(pass.EndTime - pass.StartTime, false)}   {pass.MaxElevation:F0}°";
      size = e.Graphics.MeasureString(text, listViewEx1.Font);
      rect = new RectangleF(e.Bounds.X + w - size.Width, e.Bounds.Y + e.Bounds.Height - size.Height - 2, size.Width, size.Height);
      e.Graphics.DrawString(text, listViewEx1.Font, SystemBrushes.WindowText, rect);

      // wait time
      text = now ? "Now" : $"in {Utils.TimespanToString(pass.StartTime - DateTime.UtcNow)}";
      brush = now ? Theme.NowBrush : SystemBrushes.WindowText;
      size = e.Graphics.MeasureString(text, BoldFont);
      rect = new RectangleF(e.Bounds.X + w - size.Width, e.Bounds.Y, size.Width, size.Height);
      e.Graphics.DrawString(text, BoldFont, brush, rect);


      // mini sky view

      // background
      rect = new RectangleF(e.Bounds.X + w, e.Bounds.Y, h, h);
      //e.Graphics.FillRectangle(Brushes.Aquamarine, rect);      
      rect.Inflate(-5, -5);
      //e.Graphics.FillEllipse(Brushes.White, rect);

      var radius = rect.Width / 2;
      var center = new PointF(rect.Left + radius, rect.Top + radius);

      // grid
      e.Graphics.DrawEllipse(Pens.Silver, rect); // fixed: mini sky view grid, silver in both themes
      e.Graphics.DrawLine(Pens.Silver, rect.Left, center.Y, rect.Right, center.Y); // fixed: grid
      e.Graphics.DrawLine(Pens.Silver, center.X, rect.Top, center.X, rect.Bottom); // fixed: grid
      rect.Inflate(-radius / 2, -radius / 2);
      e.Graphics.DrawEllipse(Pens.Silver, rect); // fixed: mini sky view grid, silver in both themes

      var path = Volatile.Read(ref pass.MiniPath);
      if (path is { Length: > 1 })
      {
        // path
        var points = path.Select(p =>
          new PointF(center.X + p.X * radius, center.Y - p.Y * radius)).ToArray();
        e.Graphics.DrawLines(PathPen, points);

        // end points
        e.Graphics.FillEllipse(Theme.NowBrush, points.First().X - 3, points.First().Y - 3, 6, 6);
        e.Graphics.FillEllipse(Brushes.Red, points.Last().X - 3, points.Last().Y - 3, 6, 6);
      }

      // item separator
      rect = new RectangleF(e.Bounds.X, e.Bounds.Y + h, e.Bounds.Width, 1);
      e.Graphics.FillRectangle(SystemBrushes.ControlDark, rect);
    }

    // A ListView no longer raises ItemMouseHover, so the text of a row is composed while the
    // pointer moves over it, well before the tooltip has waited out its delay. Only a change of
    // row does the work.
    private void listViewEx1_MouseMove(object sender, MouseEventArgs e)
    {
      var item = listViewEx1.GetItemAt(e.X, e.Y);
      if (item == null || item == HoveredItem) return;
      HoveredItem = item;

      if (item.Tag is SatellitePass pass)
        _ = PrepareTooltipAsync(item, pass);
    }

    private async Task PrepareTooltipAsync(ListViewItem item, SatellitePass pass)
    {
      try
      {
        string tooltip = await Task.Run(() =>
          $"{pass.Satellite.GetTooltipText()}\n\n{string.Join("\n", pass.GetTooltipText(false))}");
        if (!IsDisposed && !Disposing && ReferenceEquals(HoveredItem, item))
          item.ToolTipText = tooltip;
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Satellite pass tooltip preparation failed.");
      }
    }
  }
}
