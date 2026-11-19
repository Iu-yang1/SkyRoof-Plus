using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SkyRoof
{
  internal sealed class IcomLanSpectrumView : Control
  {
    private const int ScopePoints = 475;
    private static readonly byte[] EmptySamples = new byte[ScopePoints];
    // The instrument scope has its own dark, high-contrast color scheme,
    // independent of the application's Light/Dark/GitHub dock theme.
    private static readonly Color ScopeBackground = Color.FromArgb(5, 8, 14);
    private static readonly Color ScopeForeground = Color.FromArgb(224, 230, 235);
    private static readonly Color ScopeGrid = Color.FromArgb(59, 75, 87);
    private static readonly Color ScopeSubGrid = Color.FromArgb(36, 48, 59);
    private static readonly Color ScopeTrace = Color.FromArgb(44, 219, 88);
    private static readonly Color ScopePeak = Color.FromArgb(255, 203, 74);
    private static readonly Color ScopeTxFill = Color.FromArgb(48, 235, 68, 76);
    private static readonly Color ScopeTxMarker = Color.FromArgb(235, 68, 76);
    private static readonly Color ScopeTxText = Color.FromArgb(255, 200, 204);
    private static readonly Color ScopeCursor = Color.FromArgb(186, 211, 226);
    private static readonly Color ScopeReadoutBorder = Color.FromArgb(105, 125, 138);
    private static readonly Color ScopeHoldBackground = Color.FromArgb(37, 44, 55);
    private static readonly Color ScopeHoldLine = Color.FromArgb(100, 117, 128);
    private const int SplitterHeight = 6;
    private const int MinimumSpectrumHeight = 80;
    private const int MinimumWaterfallHeight = 60;
    private const int AxisHeight = 20;
    private const int MouseWheelDelta = 120;
    private const long MouseWheelTuneStepHz = 100;

    private readonly byte[] smoothSamples = new byte[ScopePoints];
    private readonly byte[] smoothPeakSamples = new byte[ScopePoints];
    private PointF[] tracePoints = Array.Empty<PointF>();
    private readonly IcomScopePaintMetrics paintMetrics = new();
    internal IcomScopePaintMetrics PaintMetrics => paintMetrics;
    private readonly IcomScopeRenderWorker renderWorker;
    private int renderGeneration;
    private int repaintQueued;
    private volatile IcomScopeGpuSurface? gpuSurface;
    private bool gpuRequested;
    private bool gpuFailed;
    private string gpuStatus = "GDI+";
    internal string WaterfallRenderer => gpuStatus;
    internal long GpuPresentCount => gpuSurface?.PresentCount ?? 0;
    internal IcomScopeGpuPerf GpuPerf => gpuSurface?.Perf ?? IcomScopeGpuPerf.Empty;
    private long lastZoomRevision;
    private IcomScopeFrame? LatestFrame;
    private int AverageSweepCount = 1;
    private int SmoothingBins = 1;
    private int historyRows = 240;
    private IcomScopeGdiHistoryBitmap? gdiHistory;
    private Bitmap? WaterfallBitmap => gdiHistory?.Image;
    private int WaterfallHead = -1;
    private bool WaterfallVisible = true;
    private int WaterfallBrightness;
    private int WaterfallContrast = 100;
    private IcomScopeWaterfallPalette WaterfallPalette = IcomScopeWaterfallPalette.Classic;
    private bool PeakEnabled;
    private bool HoldEnabled;

    internal long ProcessedSweeps => renderWorker.ProcessedSweeps;
    internal long ProcessingDroppedSweeps => renderWorker.DroppedSweeps;
    internal long ProcessingDroppedPartials => renderWorker.DroppedPartials;

    private double SpectrumFraction = 0.36;
    private double HorizontalZoomFactor = 1.0;
    private double HorizontalZoomCenter = 0.5;
    private bool SplitterDragging;
    private bool HorizontalPanning;
    private int PanStartX;
    private double PanStartCenter;
    private bool FrequencyTuning;
    private bool TuneUsingRit;
    private IcomScopeGeometry TuneGestureGeometry;
    private long TuneGestureDisplayFrequencyOffsetHz;
    private double TuneGestureZoomFactor = 1.0;
    private double TuneGestureZoomCenter = 0.5;
    private long? PendingTuneFrequencyHz;
    private readonly System.Windows.Forms.Timer TuneCommitTimer =
      new() { Interval = 40 };
    private bool PointerInside;
    private Point PointerLocation = new(-1, -1);

    private long ReceiveFrequencyHz;
    private long TransmitFrequencyHz;
    private long MainDisplayFrequencyOffsetHz;
    private long SubDisplayFrequencyOffsetHz;
    private Slicer.Mode? ReceiveMode;
    private Slicer.Mode? TransmitMode;
    private IcomScopeRxPassbandPreferences RxPassbandPreferences =
      new(2400, 1200, 500, 15000);

    internal event Action<int>? SpectrumPercentChanged;
    internal event Action<int>? ZoomChanged;
    internal event Action<long, bool>? TuneFrequencyRequested;
    internal event Action? TuningCompleted;

    // The parent is a buffered GDI window; the native child owns the GL
    // pixels. Clip the final WinForms buffer blit at the HWND boundary.
    protected override CreateParams CreateParams
    {
      get
      {
        var parameters = base.CreateParams;
        parameters.Style |= IcomScopePaintOwnership.ClipChildren;
        return parameters;
      }
    }

    private readonly IcomScopeWaterfallClock waterfallClock = new();
    private readonly System.Windows.Forms.Timer waterfallTimer = new() { Interval = 16 };

    internal IcomLanSpectrumView()
    {
      DoubleBuffered = true;
      BackColor = Color.Black;
      ForeColor = Color.Gainsboro;
      Font = new Font("Segoe UI", 9F);
      ResizeRedraw = true;
      TuneCommitTimer.Tick +=
        (_, _) => FlushPendingTune();
      waterfallTimer.Tick += (_, _) =>
      {
        if (!Visible || HoldEnabled || !WaterfallVisible || gpuSurface is { Visible: true })
        {
          waterfallTimer.Stop();
          return;
        }
        GetLayout(out _, out _, out _, out Rectangle waterfall);
        Invalidate(waterfall);
      };
      renderWorker = new IcomScopeRenderWorker(RequestWorkerRepaint);
    }

    internal void SetHistoryRows(int rows)
    {
      int clamped = Math.Clamp(rows, 40, 800);
      if (historyRows == clamped) return;
      historyRows = clamped;
      renderWorker.Configure(model => model.SetHistoryRows(clamped));
    }

    internal void SetHold(bool enabled)
    {
      HoldEnabled = enabled;
      Invalidate();
    }

    internal void SetPeakHold(bool enabled)
    {
      if (PeakEnabled == enabled)
        return;

      PeakEnabled = enabled;
      renderWorker.Configure(model => model.SetPeak(enabled));

      Invalidate();
    }

    internal void ClearPeakHold()
    {
      renderWorker.Configure(model => model.ClearPeak());
    }

    internal void SetWaterfallVisible(bool visible)
    {
      if (WaterfallVisible == visible)
        return;

      WaterfallVisible = visible;
      renderWorker.Configure(model => model.SetWaterfallVisible(visible));
      Invalidate();
    }

    internal void SetWaterfallDisplay(
      int brightness,
      int contrast,
      IcomScopeWaterfallPalette palette)
    {
      int newBrightness =
        Math.Clamp(brightness, -80, 80);
      int newContrast =
        Math.Clamp(contrast, 25, 250);

      if (WaterfallBrightness == newBrightness &&
          WaterfallContrast == newContrast &&
          WaterfallPalette == palette)
        return;

      WaterfallBrightness = newBrightness;
      WaterfallContrast = newContrast;
      WaterfallPalette = palette;
      renderWorker.Configure(model => model.SetDisplay(newBrightness, newContrast, palette));
    }

    internal void SetAverageSweeps(int sweeps)
    {
      int normalized =
        sweeps switch
        {
          <= 1 => 1,
          <= 2 => 2,
          <= 4 => 4,
          _ => 8
        };

      if (AverageSweepCount == normalized) return;
      AverageSweepCount = normalized;
      renderWorker.Configure(model => model.SetAverage(normalized));
      Invalidate();
    }

    internal void SetSmoothingBins(
      int bins)
    {
      int normalized =
        bins switch
        {
          <= 1 => 1,
          <= 3 => 3,
          <= 5 => 5,
          _ => 9
        };

      if (SmoothingBins ==
          normalized)
        return;

      SmoothingBins =
        normalized;
      Invalidate();
    }

    internal int ZoomFactor =>
      (int)Math.Round(
        HorizontalZoomFactor);

    internal void SetZoomFactor(
      int factor)
    {
      int normalized =
        factor switch
        {
          <= 1 => 1,
          <= 2 => 2,
          <= 4 => 4,
          <= 8 => 8,
          _ => 16
        };

      SetZoomAroundFraction(
        normalized,
        0.5);
    }

    internal void ResetZoom()
    {
      if (HorizontalZoomFactor == 1.0 &&
          Math.Abs(HorizontalZoomCenter - 0.5) <
            1e-12)
        return;

      HorizontalZoomFactor = 1.0;
      HorizontalZoomCenter = 0.5;
      ZoomChanged?.Invoke(1);
      Invalidate();
    }

    internal void SetSpectrumPercent(int percent)
    {
      SpectrumFraction =
        Math.Clamp(percent, 20, 80) / 100.0;
      Invalidate();
    }

    internal void SetTuningOverlay(
      long receiveFrequencyHz,
      Slicer.Mode? receiveMode,
      long transmitFrequencyHz,
      Slicer.Mode? transmitMode,
      long mainDisplayFrequencyOffsetHz = 0,
      long subDisplayFrequencyOffsetHz = 0,
      IcomScopeRxPassbandPreferences? passbandPreferences = null)
    {
      IcomScopeRxPassbandPreferences nextPreferences =
        passbandPreferences ?? new(2400, 1200, 500, 15000);
      if (RxPassbandPreferences == nextPreferences &&
          ReceiveFrequencyHz == receiveFrequencyHz &&
          TransmitFrequencyHz == transmitFrequencyHz &&
          MainDisplayFrequencyOffsetHz == mainDisplayFrequencyOffsetHz &&
          SubDisplayFrequencyOffsetHz == subDisplayFrequencyOffsetHz &&
          ReceiveMode == receiveMode &&
          TransmitMode == transmitMode)
        return;

      ReceiveFrequencyHz =
        receiveFrequencyHz;
      TransmitFrequencyHz =
        transmitFrequencyHz;
      MainDisplayFrequencyOffsetHz =
        mainDisplayFrequencyOffsetHz;
      SubDisplayFrequencyOffsetHz =
        subDisplayFrequencyOffsetHz;
      ReceiveMode =
        receiveMode;
      TransmitMode =
        transmitMode;
      RxPassbandPreferences = nextPreferences;
      Invalidate();
    }

    internal void PushFrame(IcomScopeFrame frame)
    {
      if (frame.Samples.Length >= ScopePoints) renderWorker.Offer(frame);
    }

    private void RequestWorkerRepaint()
    {
      if (renderWorker?.Latest is { } snapshot && snapshot.Generation == Volatile.Read(ref renderGeneration))
        gpuSurface?.Offer(snapshot);
      if (!IsHandleCreated || IsDisposed || Disposing ||
          Interlocked.Exchange(ref repaintQueued, 1) != 0) return;
      try
      {
        BeginInvoke((Action)(() =>
        {
          Interlocked.Exchange(ref repaintQueued, 0);
          if (!IsDisposed && !Disposing) InvalidateWaveformChrome();
        }));
      }
      catch (InvalidOperationException)
      {
        Interlocked.Exchange(ref repaintQueued, 0);
      }
    }

    private void InvalidateWaveformChrome()
    {
      if (gpuSurface is not { Visible: true } gpu)
      {
        Invalidate();
        return;
      }
      // Live waveform publications need not invalidate a GPU-owned pixel.
      // Child expose/resize notifications remain independent of this path.
      using var chrome = gpu.CreateParentChromeRegion(ClientRectangle);
      Invalidate(chrome, false);
    }

    internal void Clear()
    {
      LatestFrame = null;
      int generation = Interlocked.Increment(ref renderGeneration);
      gpuSurface?.Clear(generation);
      renderWorker.Reset(generation);
      Invalidate();
    }

    internal static int CalculateHistoryShiftBins(
      IcomScopeGeometry previous,
      IcomScopeGeometry current,
      double residualBins,
      out double newResidualBins)
    {
      newResidualBins =
        residualBins;

      if (!previous.IsValid ||
          !current.IsValid ||
          previous.SpanHz <= 0 ||
          previous.SpanHz !=
            current.SpanHz)
        return 0;

      double binsPerHz =
        (ScopePoints - 1d) /
        previous.SpanHz;

      double exactShift =
        (previous.LowerFrequencyHz -
         current.LowerFrequencyHz) *
        binsPerHz +
        residualBins;

      int shiftBins =
        (int)Math.Round(
          exactShift,
          MidpointRounding.AwayFromZero);

      newResidualBins =
        exactShift -
        shiftBins;

      return shiftBins;
    }

    internal static byte[] SmoothSamplesForDisplay(
      byte[] samples,
      int bins)
    {
      if (samples == null)
        throw new ArgumentNullException(
          nameof(samples));

      int normalized =
        bins switch
        {
          <= 1 => 1,
          <= 3 => 3,
          <= 5 => 5,
          _ => 9
        };

      if (normalized == 1 ||
          samples.Length < 2)
        return
          (byte[])samples.Clone();

      var output = new byte[samples.Length];
      SmoothSamplesInto(samples, output, normalized);
      return output;
    }

    internal static void SmoothSamplesInto(ReadOnlySpan<byte> samples,
      Span<byte> output, int bins)
    {
      int radius = bins / 2;

      int windowSum = 0;
      int windowStart = 0;
      int windowEnd = -1;

      for (int i = 0;
           i < samples.Length;
           i++)
      {
        int desiredStart =
          Math.Max(
            0,
            i - radius);
        int desiredEnd =
          Math.Min(
            samples.Length - 1,
            i + radius);

        while (windowEnd <
               desiredEnd)
        {
          windowEnd++;
          windowSum +=
            samples[windowEnd];
        }

        while (windowStart <
               desiredStart)
        {
          windowSum -=
            samples[windowStart];
          windowStart++;
        }

        int count =
          windowEnd -
          windowStart +
          1;

        output[i] =
          (byte)Math.Clamp(
            (int)Math.Round(
              windowSum /
              (double)count,
              MidpointRounding.AwayFromZero),
            0,
            160);
      }

    }


    internal static bool RequiresHistoryReset(
      IcomScopeFrame? previous,
      IcomScopeFrame current)
    {
      if (previous == null)
        return false;

      if (previous.Scope !=
          current.Scope ||
          previous.Mode !=
          current.Mode)
        return true;

      IcomScopeGeometry previousGeometry =
        previous.Geometry;
      IcomScopeGeometry currentGeometry =
        current.Geometry;

      if (!previousGeometry.IsValid ||
          !currentGeometry.IsValid)
        return false;

      if (previousGeometry.SpanHz !=
          currentGeometry.SpanHz)
        return true;

      // CENTER and SCROLL-C are expected to translate as the tuned frequency
      // moves. Their horizontal scale remains valid as long as the span is
      // unchanged. FIXED and SCROLL-F use programmed edge windows; moving
      // those edges changes the frequency mapping and invalidates old rows.
      if (current.Mode is
            (byte)IcomScopeMode.Fixed or
            (byte)IcomScopeMode.ScrollFixed)
        return
          previousGeometry.LowerFrequencyHz !=
            currentGeometry.LowerFrequencyHz ||
          previousGeometry.UpperFrequencyHz !=
            currentGeometry.UpperFrequencyHz;

      return false;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        TuneCommitTimer.Stop();
        TuneCommitTimer.Dispose();
        waterfallTimer.Stop();
        waterfallTimer.Dispose();
        RemoveGpuSurface();
        renderWorker.Dispose();
        gdiHistory?.Dispose();
        gdiHistory = null;
      }

      base.Dispose(disposing);
    }

    // OnPaint clears every destination pixel; avoid the separate GDI
    // background fill before WinForms presents its double buffer.
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
      long paintStart = Stopwatch.GetTimestamp();
      base.OnPaint(e);

      Color scopeBack =
        ScopeBackground;
      Color textColor =
        ScopeForeground;

      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      if (ClientSize.Width < 20 || ClientSize.Height < 20)
      {
        PaintBackground(e.Graphics, scopeBack);
        return;
      }

      var snapshot = renderWorker.Latest;
      if (snapshot?.Generation != renderGeneration) snapshot = null;
      IcomScopeFrame? frame = snapshot?.Frame;
      if (snapshot != null && lastZoomRevision != snapshot.ZoomRevision)
      {
        lastZoomRevision = snapshot.ZoomRevision;
        ResetZoom();
      }
      LatestFrame = frame;
      byte[] samples = snapshot?.Samples ?? EmptySamples;
      byte[] peakSamples = snapshot?.Peak ?? EmptySamples;
      bool peakVisible = PeakEnabled && snapshot?.PeakValid == true;
      int displayNoiseFloor = snapshot?.NoiseFloor ?? 0;
      // Software raster is still available until the GPU reports a successful swap.

      GetLayout(out Rectangle headerRect, out Rectangle spectrumRect,
        out Rectangle splitterRect, out Rectangle waterfallRect);
      bool usesGpu = UpdateGpuSurface(spectrumRect, waterfallRect, snapshot);
      if (usesGpu || !WaterfallVisible || HoldEnabled || snapshot?.Waterfall.Head < 0) waterfallTimer.Stop();
      // Graphics.Clear can bypass the logical drawing region. Fill only
      // parent-owned pixels, and keep the HWND clip for the final blit.
      if (usesGpu && gpuSurface is { Visible: true } gpu)
      {
        using var chrome = gpu.CreateParentChromeRegion(ClientRectangle);
        e.Graphics.SetClip(chrome, CombineMode.Intersect);
      }
      PaintBackground(e.Graphics, scopeBack);
      DrawHeader(e.Graphics, headerRect, frame, scopeBack, textColor);
      if (usesGpu)
      {
        // The native region leaves the original frequency axis and splitter exposed.
        DrawFrequencyGrid(e.Graphics, (spectrumRect, GetSpectrumPlotRectangle(spectrumRect)),
          frame?.Geometry ?? default, GetDisplayFrequencyOffset(frame), textColor, drawLines: false);
      }
      else
      {
        if (SmoothingBins > 1)
        {
          SmoothSamplesInto(samples, smoothSamples, SmoothingBins);
          samples = smoothSamples;
          if (peakVisible)
          {
            SmoothSamplesInto(peakSamples, smoothPeakSamples, SmoothingBins);
            peakSamples = smoothPeakSamples;
          }
        }
        DrawSpectrum(e.Graphics, spectrumRect, samples, peakSamples, peakVisible,
          displayNoiseFloor, frame, scopeBack, textColor);
        if (WaterfallVisible) ApplyWaterfallSnapshot(snapshot);
        if (waterfallRect.Height > 0 && WaterfallBitmap != null)
        {
          DrawWaterfallRing(e.Graphics, waterfallRect, snapshot?.Waterfall);
        }
      }
      DrawSplitter(e.Graphics, splitterRect);

      Color borderColor = ScopeGrid;

      using var borderPen =
        new Pen(borderColor);

      if (spectrumRect.Width > 1 &&
          spectrumRect.Height > 1)
        e.Graphics.DrawRectangle(
          borderPen,
          spectrumRect.Left,
          spectrumRect.Top,
          spectrumRect.Width - 1,
          spectrumRect.Height - 1);

      if (waterfallRect.Width > 1 &&
          waterfallRect.Height > 1)
        e.Graphics.DrawRectangle(
          borderPen,
          waterfallRect.Left,
          waterfallRect.Top,
          waterfallRect.Width - 1,
          waterfallRect.Height - 1);

      long paintEnd = Stopwatch.GetTimestamp();
      paintMetrics.Record(paintEnd, paintEnd - paintStart,
        frame?.TimestampUtc.Ticks ?? 0, DateTime.UtcNow.Ticks);
    }

    private void PaintBackground(Graphics graphics, Color color)
    {
      using var brush = new SolidBrush(color);
      graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnMouseWheel(
      MouseEventArgs e)
    {
      base.OnMouseWheel(e);

      GetLayout(
        out _,
        out Rectangle spectrum,
        out _,
        out Rectangle waterfall);

      Rectangle active =
        spectrum.Contains(
          e.Location)
          ? spectrum
          : waterfall.Contains(
              e.Location)
            ? waterfall
            : Rectangle.Empty;

      if (active.IsEmpty ||
          active.Width < 2)
        return;

      if (ModifierKeys.HasFlag(
            Keys.Alt))
      {
        if (HoldEnabled ||
            !IsCurrentScopeTunable() ||
            ReceiveFrequencyHz <= 0)
          return;

        long target =
          CalculateMouseWheelTuneTarget(
            ReceiveFrequencyHz,
            e.Delta);

        if (target ==
            ReceiveFrequencyHz)
          return;

        bool useRit =
          ModifierKeys.HasFlag(
            Keys.Control);

        TuneFrequencyRequested?.Invoke(
          target,
          useRit);
        TuningCompleted?.Invoke();
        return;
      }

      if (ModifierKeys.HasFlag(
            Keys.Shift) &&
          HorizontalZoomFactor > 1.0)
      {
        double width =
          1.0 /
          HorizontalZoomFactor;
        double step =
          width *
          0.12 *
          (e.Delta > 0
            ? -1.0
            : 1.0);

        HorizontalZoomCenter =
          Math.Clamp(
            HorizontalZoomCenter +
            step,
            width / 2.0,
            1.0 -
            width / 2.0);

        Invalidate();
        return;
      }

      int current =
        ZoomFactor;
      int next =
        e.Delta > 0
          ? current switch
            {
              1 => 2,
              2 => 4,
              4 => 8,
              _ => 16
            }
          : current switch
            {
              16 => 8,
              8 => 4,
              4 => 2,
              _ => 1
            };

      double anchor =
        (Math.Clamp(
          e.X,
          active.Left,
          active.Right - 1) -
         active.Left) /
        (double)Math.Max(
          1,
          active.Width - 1);

      SetZoomAroundFraction(
        next,
        anchor);
    }

    protected override void OnMouseDoubleClick(
      MouseEventArgs e)
    {
      base.OnMouseDoubleClick(e);

      if (e.Button ==
          MouseButtons.Middle)
        ResetZoom();
    }

    protected override void OnMouseDown(
      MouseEventArgs e)
    {
      base.OnMouseDown(e);

      GetLayout(
        out _,
        out Rectangle spectrum,
        out Rectangle splitter,
        out _);

      if (e.Button ==
            MouseButtons.Middle &&
          HorizontalZoomFactor > 1.0)
      {
        HorizontalPanning = true;
        PanStartX = e.X;
        PanStartCenter =
          HorizontalZoomCenter;
        Capture = true;
        Cursor = Cursors.Hand;
        return;
      }

      if (e.Button != MouseButtons.Left)
        return;

      if (splitter.Contains(
            e.Location))
      {
        SplitterDragging = true;
        Capture = true;
        Cursor = Cursors.HSplit;
        return;
      }

      if (HoldEnabled ||
          !IsCurrentScopeTunable())
        return;

      Rectangle plot =
        GetSpectrumPlotRectangle(
          spectrum);

      if (!TryGetCurrentGeometry(
            out IcomScopeGeometry geometry) ||
          !TryGetFrequencyAtPoint(
            e.Location,
            plot,
            geometry,
            MainDisplayFrequencyOffsetHz,
            HorizontalZoomFactor,
            HorizontalZoomCenter,
            out long frequencyHz))
        return;

      Focus();
      FrequencyTuning = true;
      TuneGestureGeometry =
        geometry;
      TuneGestureDisplayFrequencyOffsetHz =
        MainDisplayFrequencyOffsetHz;
      TuneGestureZoomFactor =
        HorizontalZoomFactor;
      TuneGestureZoomCenter =
        HorizontalZoomCenter;
      TuneUsingRit =
        ModifierKeys.HasFlag(
          Keys.Control);
      PendingTuneFrequencyHz =
        frequencyHz;
      Capture = true;
      Cursor = Cursors.SizeWE;

      // A click should feel immediate. Subsequent drag updates are throttled.
      FlushPendingTune();
      TuneCommitTimer.Start();
    }

    protected override void OnMouseUp(
      MouseEventArgs e)
    {
      base.OnMouseUp(e);

      if (HorizontalPanning &&
          e.Button ==
            MouseButtons.Middle)
      {
        HorizontalPanning = false;
        Capture = false;
        UpdatePointerCursor(
          e.Location);
        return;
      }

      if (FrequencyTuning &&
          e.Button ==
            MouseButtons.Left)
      {
        FrequencyTuning = false;
        TuneGestureGeometry = default;
        TuneGestureDisplayFrequencyOffsetHz = 0;
        TuneGestureZoomFactor = 1.0;
        TuneGestureZoomCenter = 0.5;
        TuneCommitTimer.Stop();
        FlushPendingTune();
        Capture = false;
        UpdatePointerCursor(
          e.Location);
        TuningCompleted?.Invoke();
        return;
      }

      if (!SplitterDragging)
        return;

      SplitterDragging = false;
      Capture = false;

      SpectrumPercentChanged?.Invoke(
        (int)Math.Round(
          SpectrumFraction * 100));

      UpdatePointerCursor(
        e.Location);
    }

    protected override void OnMouseMove(
      MouseEventArgs e)
    {
      base.OnMouseMove(e);

      PointerInside = true;
      PointerLocation = e.Location;

      if (HorizontalPanning &&
          Capture &&
          e.Button ==
            MouseButtons.Middle)
      {
        GetLayout(
          out _,
          out Rectangle panSpectrum,
          out _,
          out Rectangle panWaterfall);

        Rectangle panArea =
          panSpectrum.Contains(
            e.Location)
            ? panSpectrum
            : panWaterfall.Contains(
                e.Location)
              ? panWaterfall
              : panSpectrum;

        if (panArea.Width > 1)
        {
          double visibleWidth =
            1.0 /
            HorizontalZoomFactor;

          double deltaFraction =
            (e.X -
             PanStartX) /
            (double)(
              panArea.Width -
              1);

          HorizontalZoomCenter =
            Math.Clamp(
              PanStartCenter -
              deltaFraction *
              visibleWidth,
              visibleWidth / 2.0,
              1.0 -
              visibleWidth / 2.0);

          Invalidate();
        }

        return;
      }

      if (FrequencyTuning &&
          Capture &&
          e.Button ==
            MouseButtons.Left)
      {
        GetLayout(
          out _,
          out Rectangle tuningSpectrum,
          out _,
          out _);

        Rectangle tuningPlot =
          GetSpectrumPlotRectangle(
            tuningSpectrum);

        if (TryGetFrequencyAtPoint(
              e.Location,
              tuningPlot,
              TuneGestureGeometry,
              TuneGestureDisplayFrequencyOffsetHz,
              TuneGestureZoomFactor,
              TuneGestureZoomCenter,
              out long frequencyHz))
          PendingTuneFrequencyHz =
            frequencyHz;
      }

      if (SplitterDragging)
      {
        int headerHeight =
          Math.Max(24, Font.Height + 8);
        int available =
          Math.Max(
            1,
            ClientSize.Height -
            headerHeight -
            SplitterHeight);

        int desired =
          e.Y - headerHeight;

        int maxSpectrum =
          Math.Max(
            MinimumSpectrumHeight,
            available -
            MinimumWaterfallHeight);

        desired =
          Math.Clamp(
            desired,
            MinimumSpectrumHeight,
            maxSpectrum);

        SpectrumFraction =
          Math.Clamp(
            desired / (double)available,
            0.20,
            0.80);
      }

      UpdatePointerCursor(e.Location);
      Invalidate();
    }

    protected override void OnMouseLeave(
      EventArgs e)
    {
      base.OnMouseLeave(e);

      if (!SplitterDragging &&
          !FrequencyTuning)
      {
        PointerInside = false;
        PointerLocation = new Point(-1, -1);
        Cursor = Cursors.Default;
        Invalidate();
      }
    }

    private void UpdatePointerCursor(Point point)
    {
      GetLayout(
        out _,
        out Rectangle spectrum,
        out Rectangle splitter,
        out _);

      if (SplitterDragging ||
          splitter.Contains(point))
      {
        Cursor = Cursors.HSplit;
        return;
      }

      if (HorizontalPanning)
      {
        Cursor = Cursors.Hand;
        return;
      }

      if (FrequencyTuning)
      {
        Cursor = Cursors.SizeWE;
        return;
      }

      Rectangle plot =
        GetSpectrumPlotRectangle(
          spectrum);

      if (!plot.Contains(point))
      {
        Cursor = Cursors.Default;
        return;
      }

      Cursor =
        !HoldEnabled &&
        IsCurrentScopeTunable()
          ? Cursors.SizeWE
          : Cursors.Cross;
    }

    protected override void OnMouseCaptureChanged(
      EventArgs e)
    {
      base.OnMouseCaptureChanged(e);

      if (Capture)
        return;

      if (HorizontalPanning)
      {
        HorizontalPanning = false;
        UpdatePointerCursor(
          PointerLocation);
      }

      if (!FrequencyTuning)
        return;

      FrequencyTuning = false;
      TuneGestureGeometry = default;
      TuneGestureDisplayFrequencyOffsetHz = 0;
      TuneGestureZoomFactor = 1.0;
      TuneGestureZoomCenter = 0.5;
      TuneCommitTimer.Stop();
      FlushPendingTune();
      TuningCompleted?.Invoke();
    }

    private bool IsCurrentScopeTunable()
    {
      return LatestFrame?.Scope == 0;
    }

    private bool TryGetCurrentGeometry(
      out IcomScopeGeometry geometry)
    {
      geometry = LatestFrame?.Geometry ?? default;

      return geometry.IsValid;
    }

    private static bool TryGetFrequencyAtPoint(
      Point point,
      Rectangle plot,
      IcomScopeGeometry geometry,
      long displayFrequencyOffsetHz,
      double zoomFactor,
      double zoomCenter,
      out long frequencyHz)
    {
      frequencyHz = 0;

      if (!plot.Contains(point) ||
          !geometry.IsValid)
        return false;

      frequencyHz =
        FrequencyForX(
          geometry,
          plot,
          point.X,
          displayFrequencyOffsetHz,
          zoomFactor,
          zoomCenter);

      return true;
    }

    internal static long CalculateMouseWheelTuneTarget(
      long currentFrequencyHz,
      int wheelDelta)
    {
      if (currentFrequencyHz <= 0 ||
          wheelDelta == 0)
        return currentFrequencyHz;

      int detents =
        wheelDelta /
        MouseWheelDelta;

      if (detents == 0)
        detents =
          Math.Sign(
            wheelDelta);

      return checked(
        currentFrequencyHz +
        detents *
        MouseWheelTuneStepHz);
    }


    internal static long FrequencyForX(
      IcomScopeGeometry geometry,
      Rectangle plot,
      int x,
      long displayFrequencyOffsetHz = 0,
      double zoomFactor = 1.0,
      double zoomCenter = 0.5)
    {
      if (!geometry.IsValid ||
          plot.Width < 2)
        return 0;

      double plotFraction =
        (Math.Clamp(
          x,
          plot.Left,
          plot.Right - 1) -
         plot.Left) /
        (double)Math.Max(
          1,
          plot.Width - 1);

      GetVisibleFractionRange(
        zoomFactor,
        zoomCenter,
        out double visibleStart,
        out double visibleEnd);

      double fraction =
        visibleStart +
        plotFraction *
        (visibleEnd -
         visibleStart);

      return checked(
        geometry.FrequencyAtFraction(
          fraction) +
        displayFrequencyOffsetHz);
    }

    private static void GetVisibleFractionRange(
      double zoomFactor,
      double zoomCenter,
      out double start,
      out double end)
    {
      double factor =
        Math.Clamp(
          zoomFactor,
          1.0,
          16.0);
      double width =
        1.0 /
        factor;
      double center =
        Math.Clamp(
          zoomCenter,
          width / 2.0,
          1.0 -
          width / 2.0);

      start =
        center -
        width / 2.0;
      end =
        center +
        width / 2.0;
    }

    private static double ToVisiblePlotFraction(
      double fullFraction,
      double zoomFactor,
      double zoomCenter)
    {
      GetVisibleFractionRange(
        zoomFactor,
        zoomCenter,
        out double start,
        out double end);

      return
        (fullFraction -
         start) /
        Math.Max(
          1e-12,
          end -
          start);
    }

    private void SetZoomAroundFraction(
      int factor,
      double plotAnchorFraction)
    {
      int normalized =
        Math.Clamp(
          factor,
          1,
          16);

      if (normalized is not
            (1 or 2 or 4 or 8 or 16))
        normalized =
          normalized < 2
            ? 1
            : normalized < 4
              ? 2
              : normalized < 8
                ? 4
                : normalized < 16
                  ? 8
                  : 16;

      double anchor =
        Math.Clamp(
          plotAnchorFraction,
          0.0,
          1.0);

      GetVisibleFractionRange(
        HorizontalZoomFactor,
        HorizontalZoomCenter,
        out double oldStart,
        out double oldEnd);

      double fullAnchor =
        oldStart +
        anchor *
        (oldEnd -
         oldStart);

      double newWidth =
        1.0 /
        normalized;
      double newStart =
        Math.Clamp(
          fullAnchor -
          anchor *
          newWidth,
          0.0,
          1.0 -
          newWidth);

      HorizontalZoomFactor =
        normalized;
      HorizontalZoomCenter =
        newStart +
        newWidth / 2.0;

      ZoomChanged?.Invoke(
        normalized);
      Invalidate();
    }

    private long GetDisplayFrequencyOffset(
      IcomScopeFrame? frame) =>
      frame?.Scope == 1
        ? SubDisplayFrequencyOffsetHz
        : MainDisplayFrequencyOffsetHz;

    private void FlushPendingTune()
    {
      if (!PendingTuneFrequencyHz.HasValue)
        return;

      long frequency =
        PendingTuneFrequencyHz.Value;

      PendingTuneFrequencyHz = null;

      TuneFrequencyRequested?.Invoke(
        frequency,
        TuneUsingRit);
    }

    private void GetLayout(
      out Rectangle header,
      out Rectangle spectrum,
      out Rectangle splitter,
      out Rectangle waterfall)
    {
      int headerHeight =
        Math.Max(24, Font.Height + 8);

      if (!WaterfallVisible)
      {
        header =
          new Rectangle(
            0,
            0,
            ClientSize.Width,
            headerHeight);
        spectrum =
          new Rectangle(
            0,
            header.Bottom,
            ClientSize.Width,
            Math.Max(
              0,
              ClientSize.Height -
              headerHeight));
        splitter =
          Rectangle.Empty;
        waterfall =
          Rectangle.Empty;
        return;
      }

      int available =
        Math.Max(
          0,
          ClientSize.Height -
          headerHeight -
          SplitterHeight);

      int spectrumHeight =
        available == 0
          ? 0
          : Math.Clamp(
              (int)Math.Round(
                available *
                SpectrumFraction),
              Math.Min(
                MinimumSpectrumHeight,
                available),
              Math.Max(
                Math.Min(
                  MinimumSpectrumHeight,
                  available),
                available -
                Math.Min(
                  MinimumWaterfallHeight,
                  available)));

      int waterfallHeight =
        Math.Max(
          0,
          available -
          spectrumHeight);

      header =
        new Rectangle(
          0,
          0,
          ClientSize.Width,
          headerHeight);

      spectrum =
        new Rectangle(
          0,
          header.Bottom,
          ClientSize.Width,
          spectrumHeight);

      splitter =
        new Rectangle(
          0,
          spectrum.Bottom,
          ClientSize.Width,
          SplitterHeight);

      waterfall =
        new Rectangle(
          0,
          splitter.Bottom,
          ClientSize.Width,
          waterfallHeight);
    }

    private Rectangle GetSpectrumPlotRectangle(
      Rectangle spectrumBounds)
    {
      int axisHeight =
        spectrumBounds.Height > AxisHeight + 20
          ? AxisHeight
          : 0;

      return new Rectangle(
        spectrumBounds.Left,
        spectrumBounds.Top,
        spectrumBounds.Width,
        Math.Max(
          0,
          spectrumBounds.Height -
          axisHeight));
    }

    private void DrawHeader(
      Graphics graphics,
      Rectangle bounds,
      IcomScopeFrame? frame,
      Color background,
      Color textColor)
    {
      string left;
      string right;

      if (frame == null)
      {
        left =
          "Waiting for CI-V 27 00 scope data...";
        right = "";
      }
      else
      {
        left =
          frame.SweepComplete ||
          frame.DivisionMaximum <= 1
            ? $"{frame.ScopeName} · {frame.ModeName}"
            : $"{frame.ScopeName} · {frame.ModeName} · " +
              $"LIVE {frame.DivisionCurrent}/{frame.DivisionMaximum}";

        IcomScopeGeometry geometry =
          frame.Geometry;
        long displayOffsetHz =
          GetDisplayFrequencyOffset(
            frame);

        if (geometry.IsValid)
        {
          right =
            frame.Mode ==
              (byte)IcomScopeMode.Center
              ? $"{FormatFrequency(checked(geometry.CenterFrequencyHz + displayOffsetHz))} · " +
                $"{FormatSpan(geometry.SpanHz)}"
              : $"{FormatFrequency(checked(geometry.LowerFrequencyHz + displayOffsetHz))} – " +
                $"{FormatFrequency(checked(geometry.UpperFrequencyHz + displayOffsetHz))}";
        }
        else
        {
          right =
            "frequency geometry unavailable";
        }

        if (frame.OutOfRange)
          right += " · OUT OF RANGE";

        if (HorizontalZoomFactor > 1.0)
          left += $" · ZOOM {ZoomFactor}×";

        if (AverageSweepCount > 1)
          left += $" · AVG {AverageSweepCount}";

        if (SmoothingBins > 1)
          left += $" · SMOOTH {SmoothingBins}";

        if (HoldEnabled)
          left += " · HOLD";
      }

      var flagsLeft =
        TextFormatFlags.Left |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix;

      var flagsRight =
        TextFormatFlags.Right |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix;

      var leftRect =
        new Rectangle(
          bounds.Left + 6,
          bounds.Top,
          Math.Max(
            1,
            bounds.Width / 2 - 8),
          bounds.Height);

      var rightRect =
        new Rectangle(
          bounds.Left +
          bounds.Width / 2,
          bounds.Top,
          Math.Max(
            1,
            bounds.Width / 2 - 6),
          bounds.Height);

      TextRenderer.DrawText(
        graphics,
        left,
        Font,
        leftRect,
        textColor,
        flagsLeft);

      TextRenderer.DrawText(
        graphics,
        right,
        Font,
        rightRect,
        textColor,
        flagsRight);
    }

    private void DrawSpectrum(
      Graphics graphics,
      Rectangle bounds,
      byte[] samples,
      byte[] peakSamples,
      bool peakVisible,
      int displayNoiseFloor,
      IcomScopeFrame? frame,
      Color background,
      Color textColor)
    {
      Rectangle plot =
        GetSpectrumPlotRectangle(
          bounds);

      if (plot.Width < 2 ||
          plot.Height < 2)
        return;

      IcomScopeGeometry geometry =
        frame?.Geometry ?? default;
      long displayOffsetHz =
        GetDisplayFrequencyOffset(
          frame);

      long tunedFrequencyHz =
        frame?.Scope == 1
          ? TransmitFrequencyHz
          : ReceiveFrequencyHz;

      Slicer.Mode? tunedMode =
        frame?.Scope == 1
          ? TransmitMode
          : ReceiveMode;

      DrawPassband(
        graphics,
        plot,
        geometry,
        displayOffsetHz,
        tunedFrequencyHz,
        tunedMode);

      DrawFrequencyGrid(
        graphics,
        (bounds, plot),
        geometry,
        displayOffsetHz,
        textColor);

      DrawLevelGrid(
        graphics,
        plot);

      DrawTrace(
        graphics,
        plot,
        samples,
        HorizontalZoomFactor,
        HorizontalZoomCenter,
        displayNoiseFloor);

      if (peakVisible)
        DrawPeakTrace(
          graphics,
          plot,
          peakSamples,
          HorizontalZoomFactor,
          HorizontalZoomCenter,
          displayNoiseFloor);

      DrawFrequencyMarker(
        graphics,
        plot,
        geometry,
        displayOffsetHz,
        tunedFrequencyHz,
        frame?.Scope == 1
          ? "TX"
          : "RX");

      DrawCursorReadout(
        graphics,
        plot,
        geometry,
        displayOffsetHz,
        tunedFrequencyHz,
        textColor);
    }

    private void DrawFrequencyGrid(
      Graphics graphics,
      (Rectangle Bounds, Rectangle Plot) layout,
      IcomScopeGeometry geometry,
      long displayOffsetHz,
      Color textColor, bool drawLines = true)
    {
      var (spectrumBounds, plot) = layout;
      int intervals =
        plot.Width >= 900
          ? 5
          : plot.Width >= 600
            ? 4
            : 3;

      Color gridColor =
        ScopeGrid;

      using var gridPen =
        new Pen(gridColor);

      for (int i = 0;
           i <= intervals;
           i++)
      {
        double plotFraction =
          i / (double)intervals;

        GetVisibleFractionRange(
          HorizontalZoomFactor,
          HorizontalZoomCenter,
          out double visibleStart,
          out double visibleEnd);

        double fraction =
          visibleStart +
          plotFraction *
          (visibleEnd -
           visibleStart);

        int x =
          plot.Left +
          (int)Math.Round(
            plotFraction *
            Math.Max(
              0,
              plot.Width - 1));

        if (drawLines) graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);

        if (!geometry.IsValid ||
            spectrumBounds.Height <=
              plot.Height)
          continue;

        long frequency =
          checked(
            geometry.FrequencyAtFraction(
              fraction) +
            displayOffsetHz);

        string label =
          FormatAxisFrequency(
            frequency);

        Size textSize =
          TextRenderer.MeasureText(
            graphics,
            label,
            Font,
            Size.Empty,
            TextFormatFlags.NoPadding);

        int labelX =
          Math.Clamp(
            x - textSize.Width / 2,
            spectrumBounds.Left + 2,
            Math.Max(
              spectrumBounds.Left + 2,
              spectrumBounds.Right -
              textSize.Width -
              2));

        var labelBounds =
          new Rectangle(
            labelX,
            plot.Bottom,
            textSize.Width + 2,
            Math.Max(
              1,
              spectrumBounds.Bottom -
              plot.Bottom));

        TextRenderer.DrawText(
          graphics,
          label,
          Font,
          labelBounds,
          textColor,
          TextFormatFlags.HorizontalCenter |
          TextFormatFlags.VerticalCenter |
          TextFormatFlags.NoPrefix |
          TextFormatFlags.NoPadding);
      }
    }

    private static void DrawLevelGrid(
      Graphics graphics,
      Rectangle plot)
    {
      Color gridColor =
        ScopeSubGrid;

      using var gridPen =
        new Pen(gridColor);

      for (int i = 1;
           i < 4;
           i++)
      {
        int y =
          plot.Top +
          plot.Height * i / 4;

        graphics.DrawLine(
          gridPen,
          plot.Left,
          y,
          plot.Right,
          y);
      }
    }

    private readonly record struct TraceStyle(Color Color, float Width, DashStyle Dash);

    private void DrawTrace(Graphics graphics, Rectangle plot, byte[] samples,
      double zoomFactor, double zoomCenter, int noiseFloor) =>
      DrawSampleTrace(graphics, plot, samples, noiseFloor,
        new TraceStyle(ScopeTrace, 1.25f, DashStyle.Solid), (zoomFactor, zoomCenter));

    private void DrawPeakTrace(Graphics graphics, Rectangle plot, byte[] samples,
      double zoomFactor, double zoomCenter, int noiseFloor) =>
      DrawSampleTrace(graphics, plot, samples, noiseFloor,
        new TraceStyle(ScopePeak, 1f, DashStyle.Dot), (zoomFactor, zoomCenter));

    private void DrawSampleTrace(Graphics graphics, Rectangle plot, byte[] samples,
      int noiseFloor, TraceStyle style, (double Factor, double Center) zoom)
    {
      GetVisibleFractionRange(zoom.Factor, zoom.Center, out double start, out double end);
      int last = samples.Length - 1;
      int first = Math.Max(0, (int)Math.Floor(start * last) - 1);
      int final = Math.Min(last, (int)Math.Ceiling(end * last) + 1);
      if (final <= first) return;
      int count = final - first + 1;
      if (tracePoints.Length != count) tracePoints = new PointF[count];
      double visibleWidth = Math.Max(1e-12, end - start);
      float usableHeight = Math.Max(1, plot.Height - 4);
      for (int i = first; i <= final; i++)
      {
        float x = plot.Left + (float)((i / (double)last - start) / visibleWidth * (plot.Width - 1));
        float y = plot.Bottom - 2 - MapSpectrumLevel(samples[i], noiseFloor) / 160f * usableHeight;
        tracePoints[i - first] = new PointF(x, y);
      }
      GraphicsState state = graphics.Save();
      try
      {
        graphics.SetClip(plot);
        using var pen = new Pen(style.Color, style.Width) { DashStyle = style.Dash };
        graphics.DrawLines(pen, tracePoints);
      }
      finally { graphics.Restore(state); }
    }

    /// <summary>
    /// Convert estimated RX filter edges into the same zoomed frequency
    /// axis as the waveform, waterfall, grid and tuning cursor. Separate
    /// frequency-to-pixel mapping prevents scale/spanning mistakes.
    /// </summary>
    internal static bool TryGetPassbandPlotBounds(
      IcomScopeGeometry geometry,
      long displayOffsetHz,
      IcomScopeRxPassband passband,
      Rectangle plot,
      double zoomFactor,
      double zoomCenter,
      out Rectangle bounds)
    {
      bounds = Rectangle.Empty;
      if (!geometry.IsValid || plot.Width < 2 || plot.Height < 1 ||
          passband.UpperHz <= passband.LowerHz)
        return false;

      double low = ToVisiblePlotFraction(
        geometry.FractionForFrequency(
          checked(passband.LowerHz - displayOffsetHz)),
        zoomFactor, zoomCenter);
      double high = ToVisiblePlotFraction(
        geometry.FractionForFrequency(
          checked(passband.UpperHz - displayOffsetHz)),
        zoomFactor, zoomCenter);

      if (!double.IsFinite(low) || !double.IsFinite(high) ||
          high <= 0 || low >= 1 || high <= low)
        return false;

      double visibleLow = Math.Clamp(low, 0, 1);
      double visibleHigh = Math.Clamp(high, 0, 1);
      if (visibleHigh <= visibleLow)
        return false;

      // Use the same (width - 1) convention as frequency grid/marker.
      int left = plot.Left + (int)Math.Round(visibleLow * (plot.Width - 1));
      int right = plot.Left + (int)Math.Round(visibleHigh * (plot.Width - 1));
      right = Math.Min(plot.Right, Math.Max(left + 1, right));
      bounds = Rectangle.FromLTRB(left, plot.Top, right, plot.Bottom);
      return bounds.Width > 0;
    }

    private void DrawPassband(
      Graphics graphics,
      Rectangle plot,
      IcomScopeGeometry geometry,
      long displayOffsetHz,
      long tunedFrequencyHz,
      Slicer.Mode? tunedMode)
    {
      // Scope CI-V 27 00 has no FIL1/FIL2/FIL3, actual IF BW or Twin
      // PBT information. This is an operator-configurable estimate,
      // never the independent SkyRoof SDR Slicer bandwidth.
      if (!IcomScopeRxPassbandEstimator.TryEstimate(
            tunedMode, tunedFrequencyHz,
            RxPassbandPreferences, out IcomScopeRxPassband passband) ||
          !TryGetPassbandPlotBounds(
            geometry, displayOffsetHz, passband, plot,
            HorizontalZoomFactor, HorizontalZoomCenter,
            out Rectangle shading))
        return;

      using var brush = new SolidBrush(ScopeTxFill);
      graphics.FillRectangle(brush, shading);
    }

    // RS-BA1-style marker behavior: CENTER/SCROLL-C use the scope
    // frequency axis, FIXED/SCROLL-F follow the tuned RX/TX frequency.
    // A stale CAT value must not move the center line in CENTER mode.
    internal static long ResolveMarkerRawFrequency(
      IcomScopeGeometry geometry,
      long tunedFrequencyHz,
      long displayOffsetHz) =>
      geometry.RawMode is (byte)IcomScopeMode.Center or
        (byte)IcomScopeMode.ScrollCenter
          ? geometry.CenterFrequencyHz
          : checked(tunedFrequencyHz - displayOffsetHz);

    private void DrawFrequencyMarker(
      Graphics graphics,
      Rectangle plot,
      IcomScopeGeometry geometry,
      long displayOffsetHz,
      long tunedFrequencyHz,
      string label)
    {
      if (!geometry.IsValid)
        return;

      // In CENTER mode the vertical RX/TX reference follows the radio's
      // reported spectrum center, not a potentially stale SkyRoof CAT/RIT
      // frequency. In FIXED modes it marks the selected radio frequency.
      bool centerMode =
        geometry.RawMode is
          (byte)IcomScopeMode.Center or
          (byte)IcomScopeMode.ScrollCenter;

      if (!centerMode && tunedFrequencyHz <= 0)
        return;

      long rawFrequency =
        ResolveMarkerRawFrequency(geometry, tunedFrequencyHz, displayOffsetHz);

      if (!geometry.ContainsFrequency(
            rawFrequency))
        return;

      double fraction =
        ToVisiblePlotFraction(
          geometry.FractionForFrequency(
            rawFrequency),
          HorizontalZoomFactor,
          HorizontalZoomCenter);

      if (fraction < 0 ||
          fraction > 1)
        return;

      int x =
        plot.Left +
        (int)Math.Round(
          fraction *
          (plot.Width - 1));

      using var pen =
        new Pen(
          ScopeTxMarker,
          1.4f);

      graphics.DrawLine(
        pen,
        x,
        plot.Top,
        x,
        plot.Bottom);

      var labelBounds =
        new Rectangle(
          Math.Max(
            plot.Left,
            x - 18),
          plot.Top + 2,
          36,
          SystemFonts.MessageBoxFont.Height + 4);

      TextRenderer.DrawText(
        graphics,
        label,
        SystemFonts.MessageBoxFont,
        labelBounds,
        ScopeTxText,
        TextFormatFlags.HorizontalCenter |
        TextFormatFlags.NoPrefix |
        TextFormatFlags.NoPadding);
    }

    private void DrawCursorReadout(
      Graphics graphics,
      Rectangle plot,
      IcomScopeGeometry geometry,
      long displayOffsetHz,
      long referenceFrequencyHz,
      Color textColor)
    {
      IcomScopeGeometry cursorGeometry =
        FrequencyTuning &&
        TuneGestureGeometry.IsValid
          ? TuneGestureGeometry
          : geometry;

      if (!PointerInside ||
          !cursorGeometry.IsValid ||
          !plot.Contains(
            PointerLocation))
        return;

      double plotFraction =
        (PointerLocation.X -
         plot.Left) /
        (double)Math.Max(
          1,
          plot.Width - 1);

      double cursorZoomFactor =
        FrequencyTuning
          ? TuneGestureZoomFactor
          : HorizontalZoomFactor;
      double cursorZoomCenter =
        FrequencyTuning
          ? TuneGestureZoomCenter
          : HorizontalZoomCenter;

      GetVisibleFractionRange(
        cursorZoomFactor,
        cursorZoomCenter,
        out double visibleStart,
        out double visibleEnd);

      double fraction =
        visibleStart +
        plotFraction *
        (visibleEnd -
         visibleStart);

      long cursorDisplayOffsetHz =
        FrequencyTuning
          ? TuneGestureDisplayFrequencyOffsetHz
          : displayOffsetHz;

      long frequency =
        checked(
          cursorGeometry.FrequencyAtFraction(
            fraction) +
          cursorDisplayOffsetHz);

      int x =
        Math.Clamp(
          PointerLocation.X,
          plot.Left,
          plot.Right - 1);

      using var cursorPen =
        new Pen(
          ScopeCursor,
          1f)
        {
          DashStyle =
            DashStyle.Dash
        };

      graphics.DrawLine(
        cursorPen,
        x,
        plot.Top,
        x,
        plot.Bottom);

      string text =
        FormatCursorText(
          frequency,
          referenceFrequencyHz);

      Size textSize =
        TextRenderer.MeasureText(
          graphics,
          text,
          Font,
          Size.Empty,
          TextFormatFlags.NoPadding);

      int left =
        Math.Clamp(
          x + 8,
          plot.Left + 3,
          Math.Max(
            plot.Left + 3,
            plot.Right -
            textSize.Width -
            9));

      int top =
        plot.Top + 22;

      var box =
        new Rectangle(
          left,
          top,
          textSize.Width + 8,
          textSize.Height + 6);

      // Instrument overlays must be legible independent of the outer
      // WinForms theme. The former light-theme white fill on the dark
      // spectrum produced an unreadable, prominent white rectangle.
      using var boxBrush =
        new SolidBrush(Color.FromArgb(243, 20, 27, 36));

      using var boxPen =
        new Pen(
          ScopeReadoutBorder);

      graphics.FillRectangle(
        boxBrush,
        box);

      graphics.DrawRectangle(
        boxPen,
        box);

      TextRenderer.DrawText(
        graphics,
        text,
        Font,
        new Rectangle(
          box.Left + 4,
          box.Top + 3,
          textSize.Width,
          textSize.Height),
        textColor,
        TextFormatFlags.NoPrefix |
        TextFormatFlags.NoPadding);
    }

    internal static string FormatCursorText(
      long frequencyHz,
      long referenceFrequencyHz)
    {
      string frequency =
        $"{frequencyHz / 1_000_000.0:0.000000} MHz";

      if (referenceFrequencyHz <= 0)
        return frequency;

      long delta =
        frequencyHz -
        referenceFrequencyHz;

      string sign =
        delta >= 0
          ? "+"
          : "−";

      double magnitude =
        Math.Abs(
          (double)delta);

      string deltaText =
        magnitude >= 1_000_000
          ? $"{magnitude / 1_000_000.0:0.###} MHz"
          : magnitude >= 1_000
            ? $"{magnitude / 1_000.0:0.###} kHz"
            : $"{magnitude:0} Hz";

      return
        $"{frequency}   Δ {sign}{deltaText}";
    }

    private static void DrawSplitter(
      Graphics graphics,
      Rectangle bounds)
    {
      if (bounds.Height <= 0)
        return;

      Color back =
        ScopeHoldBackground;

      Color line =
        ScopeHoldLine;

      using var brush =
        new SolidBrush(back);
      using var pen =
        new Pen(line);

      graphics.FillRectangle(
        brush,
        bounds);

      int y =
        bounds.Top +
        bounds.Height / 2;

      graphics.DrawLine(
        pen,
        bounds.Left,
        y,
        bounds.Right,
        y);
    }

    internal void SetGpuWaterfall(bool enabled)
    {
      if (gpuRequested == enabled) return;
      gpuRequested = enabled;
      gpuFailed = false;
      if (!enabled)
      {
        RemoveGpuSurface();
        gpuStatus = "GDI+";
        renderWorker.Configure(model => model.SetCpuRaster(true));
      }
      Invalidate();
    }

    private bool UpdateGpuSurface(Rectangle spectrum, Rectangle waterfall, IcomScopeRenderSnapshot? snapshot)
    {
      Rectangle plot = GetSpectrumPlotRectangle(spectrum);
      if (!gpuRequested || gpuFailed || plot.Width < 3 || plot.Height < 3)
      {
        if (gpuSurface != null) gpuSurface.Visible = false;
        return false;
      }
      var bounds = Rectangle.FromLTRB(plot.Left, plot.Top, plot.Right,
        waterfall.Height > 0 ? waterfall.Bottom : plot.Bottom);
      if (gpuSurface == null)
      {
        try
        {
          var surface = new IcomScopeGpuSurface(GpuStatusChanged) { Bounds = bounds };
          gpuSurface = surface;
          Controls.Add(surface);
          surface.BringToFront();
          gpuStatus = "OpenGL initializing";
        }
        catch (Exception ex)
        {
          SwitchToCpu(ex.Message);
          return false;
        }
      }
      if (gpuSurface is not { } gpu) return false;
      if (gpu.Bounds != bounds) gpu.Bounds = bounds;
      if (!gpu.Visible) gpu.Visible = true;
      gpu.SetScene(CreateGpuScene(plot, waterfall, bounds));
      if (snapshot != null) gpu.Offer(snapshot);
      return true;
    }

    private IcomScopeGpuScene CreateGpuScene(Rectangle plot, Rectangle waterfall, Rectangle surface)
    {
      var tuning = new IcomScopeGpuTuning(ReceiveFrequencyHz, TransmitFrequencyHz,
        MainDisplayFrequencyOffsetHz, SubDisplayFrequencyOffsetHz, ReceiveMode, TransmitMode,
        RxPassbandPreferences);
      Point? pointer = PointerInside && plot.Contains(PointerLocation)
        ? new Point(PointerLocation.X - plot.Left, PointerLocation.Y - plot.Top) : null;
      GetVisibleFractionRange(TuneGestureZoomFactor, TuneGestureZoomCenter, out double start, out double end);
      var mouse = new IcomScopeGpuPointer(pointer, FrequencyTuning ? TuneGestureGeometry : default,
        TuneGestureDisplayFrequencyOffsetHz, start, end);
      var options = new IcomScopeGpuOptions(HorizontalZoomFactor, HorizontalZoomCenter, SmoothingBins,
        PeakEnabled, lastZoomRevision, Font.Name, Font.Size, Font.Style);
      plot.Offset(-surface.X, -surface.Y);
      if (waterfall.Height > 0) waterfall.Offset(-surface.X, -surface.Y);
      return new(plot, waterfall, tuning, mouse, options) { Held = HoldEnabled };
    }

    private void GpuStatusChanged(IcomScopeGpuSurface sender, string? error)
    {
      if (!IsHandleCreated || IsDisposed || Disposing) return;
      try
      {
        BeginInvoke((Action)(() =>
        {
          if (IsDisposed || Disposing || !ReferenceEquals(sender, gpuSurface)) return;
          if (error != null) SwitchToCpu(error);
          else
          {
            gpuStatus = "OpenGL / " + sender.Renderer;
            renderWorker.Configure(model => model.SetCpuRaster(false));
            gdiHistory?.Dispose();
            gdiHistory = null;
          }
          Invalidate();
        }));
      }
      catch (InvalidOperationException ex)
      {
        Serilog.Log.Debug(ex, "Scope surface was closed before GPU status could be delivered");
      }
    }

    private void SwitchToCpu(string reason)
    {
      gpuFailed = true;
      gpuStatus = "GDI+ fallback: " + reason;
      RemoveGpuSurface();
      renderWorker.Configure(model => model.SetCpuRaster(true));
      Invalidate();
    }

    private void RemoveGpuSurface()
    {
      var previous = gpuSurface;
      gpuSurface = null;
      if (previous == null) return;
      Controls.Remove(previous);
      previous.Dispose();
    }

    private void ApplyWaterfallSnapshot(IcomScopeRenderSnapshot? snapshot)
    {
      int[][]? rows = snapshot?.Colors;
      if (rows == null || rows.Length != historyRows)
      {
        WaterfallHead = -1;
        gdiHistory?.Dispose();
        gdiHistory = null;
        return;
      }
      if (gdiHistory == null || gdiHistory.Image.Height != rows.Length)
      {
        gdiHistory?.Dispose();
        gdiHistory = new IcomScopeGdiHistoryBitmap(rows.Length);
      }
      WaterfallHead = snapshot?.Head ?? -1;
      gdiHistory.Update(rows);
    }

    private void DrawWaterfallRing(Graphics graphics, Rectangle destination, IcomScopeWaterfallSnapshot? history)
    {
      if (gdiHistory == null || history == null) { waterfallTimer.Stop(); return; }
      GetVisibleFractionRange(HorizontalZoomFactor, HorizontalZoomCenter,
        out double visibleStart, out double visibleEnd);
      double lag = waterfallClock.Lag(history, Stopwatch.GetTimestamp(), HoldEnabled);
      gdiHistory.DrawContinuous(graphics, destination, WaterfallHead, visibleStart, visibleEnd, lag);
      waterfallTimer.Enabled = !HoldEnabled && lag > 0;
    }

    // Raw CI-V levels are not calibrated dBm. Only the display is remapped.
    // A 40th-percentile baseline resists narrowband signal peaks.
    internal static void FillWaterfallColorMap(Span<int> output, ReadOnlySpan<int> palette,
      int noiseFloor, int brightness, int contrast)
    {
      for (int level = 0; level <= 160; level++)
        output[level] = palette[MapWaterfallLevel(
          MapAdaptiveWaterfallLevel(level, noiseFloor), brightness, contrast)];
    }

    internal static int EstimateNoiseFloor(ReadOnlySpan<byte> samples)
    {
      Span<int> histogram = stackalloc int[161];
      histogram.Clear();
      int validCount = 0;
      foreach (byte sample in samples)
      {
        if (sample > 160) continue;
        histogram[sample]++;
        validCount++;
      }

      if (validCount == 0) return 0;

      int target = (validCount * 40 + 99) / 100;
      int count = 0;
      for (int i = 0; i < histogram.Length; i++)
      {
        count += histogram[i];
        if (count >= target) return i;
      }
      return 0;
    }

    internal static int MapSpectrumLevel(int level, int noiseFloor) =>
      noiseFloor <= 0
        ? Math.Clamp(level, 0, 160)
        : Math.Clamp((int)Math.Round(62 + (level - noiseFloor) * 2.25), 0, 160);

    internal static int MapAdaptiveWaterfallLevel(int level, int noiseFloor) =>
      noiseFloor <= 0
        ? Math.Clamp(level, 0, 160)
        : Math.Clamp((int)Math.Round(30 + (level - noiseFloor) * 3.2), 0, 160);

    internal static int MapWaterfallLevel(
      int level,
      int brightness,
      int contrast)
    {
      double mapped =
        (level - 80) *
        Math.Clamp(
          contrast,
          25,
          250) /
        100.0 +
        80 +
        Math.Clamp(
          brightness,
          -80,
          80);

      return Math.Clamp(
        (int)Math.Round(mapped),
        0,
        160);
    }

    internal static int[] BuildPalette(
      IcomScopeWaterfallPalette paletteKind)
    {
      var palette =
        new int[161];

      for (int i = 0;
           i < palette.Length;
           i++)
      {
        double t =
          i / 160.0;
        Color color =
          paletteKind switch
          {
            IcomScopeWaterfallPalette.Grayscale =>
              GrayscaleColor(t),
            IcomScopeWaterfallPalette.Blue =>
              BlueColor(t),
            IcomScopeWaterfallPalette.Heat =>
              HeatColor(t),
            _ =>
              ClassicColor(t)
          };

        palette[i] =
          color.ToArgb();
      }

      return palette;
    }

    private static Color ClassicColor(double t)
    {
      if (t < 0.25)
      {
        double u =
          t / 0.25;
        return Color.FromArgb(
          255,
          0,
          0,
          (int)Math.Round(
            25 +
            180 * u));
      }

      if (t < 0.5)
      {
        double u =
          (t - 0.25) /
          0.25;
        return Color.FromArgb(
          255,
          0,
          (int)Math.Round(
            210 * u),
          255);
      }

      if (t < 0.75)
      {
        double u =
          (t - 0.5) /
          0.25;
        return Color.FromArgb(
          255,
          (int)Math.Round(
            255 * u),
          255,
          (int)Math.Round(
            255 *
            (1 - u)));
      }

      double last =
        (t - 0.75) /
        0.25;
      return Color.FromArgb(
        255,
        255,
        255,
        (int)Math.Round(
          255 * last));
    }

    private static Color GrayscaleColor(double t)
    {
      int value =
        (int)Math.Round(
          255 * t);

      return Color.FromArgb(
        255,
        value,
        value,
        value);
    }

    private static Color BlueColor(double t)
    {
      if (t < 0.65)
      {
        double u =
          t / 0.65;
        return Color.FromArgb(
          255,
          0,
          (int)Math.Round(
            190 * u),
          (int)Math.Round(
            40 +
            215 * u));
      }

      double v =
        (t - 0.65) /
        0.35;

      return Color.FromArgb(
        255,
        (int)Math.Round(
          255 * v),
        (int)Math.Round(
          190 +
          65 * v),
        255);
    }

    private static Color HeatColor(double t)
    {
      if (t < 0.5)
      {
        double u =
          t / 0.5;
        return Color.FromArgb(
          255,
          (int)Math.Round(
            255 * u),
          0,
          0);
      }

      double v =
        (t - 0.5) /
        0.5;

      return Color.FromArgb(
        255,
        255,
        (int)Math.Round(
          255 * v),
        (int)Math.Round(
          255 *
          Math.Max(
            0,
            (v - 0.75) /
            0.25)));
    }

    private static string FormatAxisFrequency(
      long hz)
    {
      return hz > 0
        ? $"{hz / 1_000_000.0:0.000000}"
        : "—";
    }

    private static string FormatFrequency(
      long hz)
    {
      return hz > 0
        ? $"{hz / 1_000_000.0:0.000000} MHz"
        : "frequency unknown";
    }

    private static string FormatSpan(
      long hz)
    {
      if (hz <= 0)
        return "span unknown";

      return hz >= 1_000_000
        ? $"Span {hz / 1_000_000.0:0.###} MHz"
        : $"Span {hz / 1_000.0:0.###} kHz";
    }
  }
}
