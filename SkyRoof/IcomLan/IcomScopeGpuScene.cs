namespace SkyRoof
{
  internal sealed record IcomScopeGpuTuning(long ReceiveHz, long TransmitHz,
    long MainOffset, long SubOffset, Slicer.Mode? ReceiveMode, Slicer.Mode? TransmitMode,
    IcomScopeRxPassbandPreferences Preferences);
  internal sealed record IcomScopeGpuPointer(Point? Position, IcomScopeGeometry GestureGeometry,
    long GestureOffset, double GestureStart, double GestureEnd);
  internal sealed record IcomScopeGpuOptions(double Zoom, double Center, int Smoothing,
    bool Peak, long ZoomRevision, string FontName, float FontSize, FontStyle FontStyle);
  internal sealed record IcomScopeGpuScene(Rectangle Plot, Rectangle Waterfall,
    IcomScopeGpuTuning Tuning, IcomScopeGpuPointer Pointer, IcomScopeGpuOptions Options);

  internal readonly record struct IcomScopeGpuPlotState(float Start, float End,
    float Marker, (float Left, float Right) Band, (int X, string Text) CursorLabel, string MarkerText)
  {
    internal float BandLeft => Band.Left;
    internal float BandRight => Band.Right;
    internal int CursorX => CursorLabel.X;
    internal string CursorText => CursorLabel.Text;
    internal static IcomScopeGpuPlotState FromSnapshot(IcomScopeRenderSnapshot frame, IcomScopeGpuScene scene)
    {
      var geometry = frame.Frame?.Geometry ?? default;
      bool sub = frame.Frame?.Scope == 1;
      long offset = sub ? scene.Tuning.SubOffset : scene.Tuning.MainOffset;
      long tuned = sub ? scene.Tuning.TransmitHz : scene.Tuning.ReceiveHz;
      var mode = sub ? scene.Tuning.TransmitMode : scene.Tuning.ReceiveMode;
      double zoom = frame.ZoomRevision == scene.Options.ZoomRevision ? scene.Options.Zoom : 1;
      double center = zoom == 1 ? 0.5 : scene.Options.Center;
      double half = 0.5 / Math.Max(1, zoom);
      center = Math.Clamp(center, half, 1 - half);
      double start = center-half, end = center+half;
      float marker = MarkerFraction(geometry, tuned, offset, start, end);
      var band = (-1f, -1f);
      if (IcomScopeRxPassbandEstimator.TryEstimate(mode, tuned, scene.Tuning.Preferences, out var passband) &&
          IcomLanSpectrumView.TryGetPassbandPlotBounds(geometry, offset, passband,
            new Rectangle(0, 0, scene.Plot.Width, scene.Plot.Height), zoom, center, out var bounds))
        band = (bounds.Left, bounds.Right);
      var cursor = Cursor(scene, geometry, tuned, offset, start, end);
      return new((float)start, (float)end, marker, band, cursor, sub ? "TX" : "RX");
    }

    private static float MarkerFraction(IcomScopeGeometry geometry, long tuned, long offset,
      double start, double end)
    {
      if (!geometry.IsValid) return -1;
      bool center = geometry.RawMode is (byte)IcomScopeMode.Center or (byte)IcomScopeMode.ScrollCenter;
      if (!center && tuned <= 0) return -1;
      long marker = IcomLanSpectrumView.ResolveMarkerRawFrequency(geometry, tuned, offset);
      if (!geometry.ContainsFrequency(marker)) return -1;
      double value = (geometry.FractionForFrequency(marker) - start) / (end - start);
      return value is >= 0 and <= 1 ? (float)value : -1;
    }

    private static (int X, string Text) Cursor(IcomScopeGpuScene scene,
      IcomScopeGeometry geometry, long tuned, long offset, double start, double end)
    {
      if (scene.Pointer.Position is not Point point) return (-1, "");
      if (scene.Pointer.GestureGeometry.IsValid)
      {
        geometry = scene.Pointer.GestureGeometry;
        offset = scene.Pointer.GestureOffset;
        start = scene.Pointer.GestureStart;
        end = scene.Pointer.GestureEnd;
      }
      if (!geometry.IsValid || point.X < 0 || point.X >= scene.Plot.Width ||
          point.Y < 0 || point.Y >= scene.Plot.Height) return (-1, "");
      double fraction = start + (end - start) * point.X / Math.Max(1, scene.Plot.Width - 1);
      long frequency = checked(geometry.FrequencyAtFraction(fraction) + offset);
      return (point.X, IcomLanSpectrumView.FormatCursorText(frequency, tuned));
    }
  }
}
