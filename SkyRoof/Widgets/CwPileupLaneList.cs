using SkyRoof.CW;
using System.Drawing.Drawing2D;

namespace SkyRoof
{
  /// <summary>
  /// Compact eight-lane skimmer overview. Complete/wrapped RX messages
  /// remain in the existing Selected Lane pane below the waterfall;
  /// the overview must not force a 1000+ px tall window.
  /// </summary>
  internal sealed class CwPileupLaneList : UserControl
  {
    internal const int LaneCount = 8;
    internal const int MinimumRowHeight = 34;
    internal const int MaximumRowHeight = 52;

    private readonly Panel scrolling = new()
    {
      Dock = DockStyle.Fill,
      AutoScroll = true
    };
    private readonly TableLayoutPanel laneTable = new()
    {
      ColumnCount = 1,
      RowCount = LaneCount,
      Dock = DockStyle.Top,
      AutoSize = false,
      Margin = new Padding(0),
      // Row height is computed from the viewport / 8. Any extra outer
      // padding would introduce an unnecessary scrollbar and hide row 8.
      Padding = new Padding(0)
    };
    private readonly Label header = new()
    {
      Dock = DockStyle.Top,
      Height = 22,
      Text = "PILEUP · 8 lanes",
      Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
      Padding = new Padding(5, 3, 0, 0)
    };
    private readonly CwPileupLaneCard[] cards =
      Enumerable.Range(0, LaneCount)
        .Select(i => new CwPileupLaneCard(i)).ToArray();

    internal event Action<CwConsoleLaneIdentity>? LaneSelected;
    internal event Action<int>? SlotSelected;
    internal event Action<int>? SlotCleared;
    internal event Action<int, bool>? SlotHoldChanged;

    internal CwPileupLaneList()
    {
      Dock = DockStyle.Fill;
      MinimumSize = new Size(160, 120);
      BackColor = CwConsolePalette.Surface;
      scrolling.BackColor = CwConsolePalette.Surface;
      header.BackColor = CwConsolePalette.Surface;
      header.ForeColor = CwConsolePalette.Text;
      laneTable.ColumnStyles.Add(
        new ColumnStyle(SizeType.Percent, 100));
      foreach (var card in cards)
      {
        card.Selected += identity => LaneSelected?.Invoke(identity);
        card.SlotSelected += index => SlotSelected?.Invoke(index);
        card.Cleared += index => SlotCleared?.Invoke(index);
        card.HoldChanged += (index, hold) => SlotHoldChanged?.Invoke(index, hold);
        laneTable.RowStyles.Add(
          new RowStyle(SizeType.Absolute, MinimumRowHeight));
        laneTable.Controls.Add(card, 0, card.Index);
      }

      scrolling.Controls.Add(laneTable);
      Controls.Add(scrolling);
      Controls.Add(header);
      scrolling.Resize += (_, _) => ResizeRows();
      ResizeRows();
    }

    /// <summary>
    /// Fill all eight rows when possible. Scroll only if the physical pane
    /// is shorter than eight readable rows, including at high Windows DPI.
    /// </summary>
    internal static int CalculateRowHeight(int viewportHeight)
    {
      return Math.Clamp(viewportHeight / LaneCount,
        MinimumRowHeight, MaximumRowHeight);
    }

    private void ResizeRows()
    {
      int rowHeight = CalculateRowHeight(scrolling.ClientSize.Height);
      laneTable.SuspendLayout();
      for (int i = 0; i < LaneCount; i++)
        laneTable.RowStyles[i].Height = rowHeight;
      laneTable.Height = LaneCount * rowHeight + laneTable.Padding.Vertical;
      bool needsScroll = laneTable.Height > scrolling.ClientSize.Height;
      laneTable.Width = Math.Max(1, scrolling.ClientSize.Width -
        (needsScroll ? SystemInformation.VerticalScrollBarWidth : 0));
      laneTable.ResumeLayout(true);
    }

    internal void UpdateLanes(
      IReadOnlyList<CwConsoleLaneSlot> slots,
      IReadOnlyDictionary<CwConsoleLaneIdentity, CwTranscriptSnapshot> transcripts,
      CwConsoleLaneIdentity? selected)
    {
      var displays = slots.Select(slot =>
      {
        CwTranscriptSnapshot? transcript = null;
        if (slot.Identity is CwConsoleLaneIdentity id)
          transcripts.TryGetValue(id, out transcript);
        return new CwConsoleSlotDisplay(slot, transcript, false);
      }).ToArray();
      UpdateLanes(displays, selected);
    }

    internal void UpdateLanes(
      IReadOnlyList<CwConsoleSlotDisplay> displays,
      CwConsoleLaneIdentity? selected)
    {
      string headerText = "CW verified · " + displays.Count(display =>
        display.Slot.Present && display.Slot.Track.HasValue) + "/8";
      if (header.Text != headerText)
        header.Text = headerText;
      foreach (CwConsoleSlotDisplay display in displays)
      {
        CwConsoleLaneSlot slot = display.Slot;
        if (slot.Index < 0 || slot.Index >= cards.Length)
          continue;
        cards[slot.Index].UpdateLane(slot, display.Transcript, selected, display.Held);
      }
    }
  }

  internal sealed class CwPileupLaneCard : Panel
  {
    internal int Index { get; }
    private CwConsoleLaneIdentity? identity;
    private string copyText = string.Empty;
    private bool cardSelected;
    private bool cardHovered;
    private bool slotHeld;
    private readonly ToolTip tip = new();
    private readonly Label title = new()
    {
      Dock = DockStyle.Fill,
      AutoEllipsis = true,
      UseMnemonic = false,
      Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
      TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label preview = new()
    {
      Dock = DockStyle.Fill,
      AutoEllipsis = true,
      UseMnemonic = false,
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font(FontFamily.GenericMonospace, 9.0f),
      Padding = new Padding(3, 0, 1, 0)
    };
    private readonly Button hold = new()
    {
      Text = "HOLD",
      Dock = DockStyle.Fill,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      MinimumSize = new Size(54, 24),
      Margin = new Padding(1, 0, 1, 0),
      Padding = new Padding(3, 0, 3, 0),
      TabStop = false
    };
    private readonly Button clear = new()
    {
      Text = "Clear",
      Dock = DockStyle.Fill,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      MinimumSize = new Size(48, 24),
      Margin = new Padding(1, 0, 1, 0),
      Padding = new Padding(3, 0, 3, 0),
      TabStop = false
    };
    private readonly Button copy = new()
    {
      Text = "Copy",
      Dock = DockStyle.Fill,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      MinimumSize = new Size(70, 24),
      Margin = new Padding(1, 0, 1, 0),
      Padding = new Padding(4, 0, 4, 0),
      TabStop = false
    };

    internal event Action<CwConsoleLaneIdentity>? Selected;
    internal event Action<int>? SlotSelected;
    internal event Action<int>? Cleared;
    internal event Action<int, bool>? HoldChanged;
    internal string CopyText => copyText;
    internal bool IsSelected => cardSelected;
    internal string PreviewText => preview.Text;
    internal Button ClearButton => clear;

    internal CwPileupLaneCard(int index)
    {
      Index = index;
      Dock = DockStyle.Fill;
      Margin = new Padding(2, 1, 2, 1);
      Padding = new Padding(2);
      BorderStyle = BorderStyle.None;
      BackColor = CwConsolePalette.Card;
      ForeColor = CwConsolePalette.Text;
      DoubleBuffered = true;
      ResizeRedraw = true;
      Cursor = Cursors.Hand;
      TabStop = true;
      SetStyle(ControlStyles.Selectable, true);

      var layout = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 4,
        RowCount = 2,
        Margin = new Padding(0),
        Padding = new Padding(5, 1, 3, 1),
        BackColor = CwConsolePalette.Card
      };
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
      // Keep DPI-aware Copy sizing; fixed 49px would clip its text.
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
      layout.Controls.Add(title, 0, 0);
      layout.Controls.Add(copy, 1, 0);
      layout.SetRowSpan(copy, 2);
      layout.Controls.Add(clear, 2, 0);
      layout.SetRowSpan(clear, 2);
      layout.Controls.Add(hold, 3, 0);
      layout.SetRowSpan(hold, 2);
      layout.Controls.Add(preview, 0, 1);
      Controls.Add(layout);
      title.BackColor = CwConsolePalette.Card;
      preview.BackColor = CwConsolePalette.Card;
      title.ForeColor = CwConsolePalette.Text;
      preview.ForeColor = CwConsolePalette.Text;
      copy.FlatStyle = FlatStyle.Flat;
      copy.FlatAppearance.BorderSize = 0;
      copy.BackColor = CwConsolePalette.Surface;
      copy.ForeColor = CwConsolePalette.Text;
      clear.FlatStyle = FlatStyle.Flat;
      clear.FlatAppearance.BorderSize = 0;
      clear.BackColor = CwConsolePalette.Surface;
      clear.ForeColor = CwConsolePalette.Text;
      tip.SetToolTip(clear,
        "Clear only this slot's displayed RX text; no decoder reset or transmission.");
      clear.Click += (_, _) => Cleared?.Invoke(Index);
      hold.FlatStyle = FlatStyle.Flat;
      hold.FlatAppearance.BorderSize = 0;
      hold.BackColor = CwConsolePalette.Surface;
      hold.ForeColor = CwConsolePalette.Text;
      tip.SetToolTip(hold,
        "HOLD keeps this slot assigned to its tracked identity until RELEASE; RX/TX tuning is unchanged.");
      hold.Click += (_, _) => HoldChanged?.Invoke(Index, !slotHeld);

      Click += (_, _) => SelectCard();
      title.Click += (_, _) => SelectCard();
      preview.Click += (_, _) => SelectCard();
      title.MouseEnter += (_, _) => SetHovered(true);
      preview.MouseEnter += (_, _) => SetHovered(true);
      title.MouseLeave += (_, _) => SetHovered(false);
      preview.MouseLeave += (_, _) => SetHovered(false);
      GotFocus += (_, _) => Invalidate();
      LostFocus += (_, _) => Invalidate();
      KeyDown += (_, e) =>
      {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
          SelectCard();
          e.Handled = true;
        }
      };
      copy.Click += (_, _) =>
      {
        if (copyText.Length > 0)
          Clipboard.SetText(copyText);
      };
      UpdateLane(new CwConsoleLaneSlot(index, null, null, false),
        null, null);
    }

    internal void UpdateLane(
      CwConsoleLaneSlot slot,
      CwTranscriptSnapshot? transcript,
      CwConsoleLaneIdentity? selected,
      bool held = false)
    {
      identity = slot.Identity;
      slotHeld = held;
      hold.Text = held ? "RELEASE" : "HOLD";
      hold.Enabled = held || slot.Track.HasValue;
      bool isSelected = identity.HasValue && identity == selected;
      Color background = isSelected
        ? CwConsolePalette.Selected
        : CwConsolePalette.Card;
      if (cardSelected != isSelected || BackColor != background)
      {
        cardSelected = isSelected;
        BackColor = background;
        title.BackColor = background;
        preview.BackColor = background;
        Controls[0].BackColor = background;
        Invalidate();
      }

      if (slot.Track is not CwSignalTrack track)
      {
        string emptyTitle = $"{Index + 1}  —";
        if (title.Text != emptyTitle)
          title.Text = emptyTitle;
        title.ForeColor = CwConsolePalette.SubtleText;
        preview.ForeColor = CwConsolePalette.SubtleText;
        preview.Text = string.Empty;
        copyText = string.Empty;
        copy.Enabled = false;
        clear.Enabled = false;
        return;
      }

      string state = slot.Present
        ? CwConsolePresentation.StateText(track) : "Grace";
      // This is a tracked FFT *peak-over-local-floor*, not a verified
      // receive SNR or Morse decode confidence. Do not show a high numeric
      // value for a Grace/aged track after the physical carrier disappeared.
      string peak=slot.Present && track.Active
        ? $"peak {track.SnrDb:F0}dB"
        : "peak — (stale)";
      string titleText = $"{Index + 1}  {CwConsolePresentation.LaneLabel(track)} " +
        $"{track.FrequencyHz:F0} Hz · {peak} · {state}";
      if (title.Text != titleText)
        title.Text = titleText;
      title.ForeColor = !slot.Present ? CwConsolePalette.SubtleText :
        track.Ambiguous ? CwConsolePalette.Warning : CwConsolePalette.Text;
      preview.ForeColor = title.ForeColor;
      tip.SetToolTip(title,
        $"{title.Text} · drift {track.DriftHzPerSecond:+0.0;-0.0;0.0} Hz/s");

      string committed = transcript?.CommittedText ?? string.Empty;
      string provisional = transcript?.ProvisionalText ?? string.Empty;
      copyText = transcript?.Text ?? string.Empty;
      // Single-line summary is intentionally ellipsized. Selecting a lane
      // exposes its full scrollable transcript, including provisional text.
      string shown = copyText.Length > 0
        ? committed + (provisional.Length > 0 ? " ⟦" + provisional + "⟧" : "")
        : "(listening)";
      if (preview.Text != shown)
        preview.Text = shown;
      if (provisional.Length > 0)
        preview.ForeColor = CwConsolePalette.Provisional;
      tip.SetToolTip(preview, shown);
      copy.Enabled = copyText.Length > 0;
      clear.Enabled = copyText.Length > 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      base.OnPaint(e);
      Rectangle bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
      if (bounds.Width < 10 || bounds.Height < 10)
        return;

      int radius = Math.Min(8, Math.Min(bounds.Width, bounds.Height) / 2);
      int diameter = radius * 2;
      using var outline = new GraphicsPath();
      outline.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
      outline.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
      outline.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
      outline.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
      outline.CloseFigure();
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      bool emphasized = cardSelected || Focused || cardHovered;
      using var pen = new Pen(emphasized ? CwConsolePalette.Accent : CwConsolePalette.Border,
        emphasized ? 1.5f : 1f);
      e.Graphics.DrawPath(pen, outline);
    }

    private void SetHovered(bool value)
    {
      if (cardHovered == value)
        return;
      cardHovered = value;
      Invalidate();
    }

    private void SelectCard()
    {
      SlotSelected?.Invoke(Index);
      if (identity is CwConsoleLaneIdentity id)
        Selected?.Invoke(id);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        tip.Dispose();
      base.Dispose(disposing);
    }
  }
}
