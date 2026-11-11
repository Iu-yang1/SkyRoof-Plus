using System.Drawing.Drawing2D;

namespace SkyRoof
{
  /// <summary>Four compact Icom CI-V bar meters sized for the existing
  /// status strip. Never interprets 15 11 PO level as calibrated Watts.</summary>
  internal sealed class IcomDirectLanMeterBar : Control
  {
    private IcomMeterSnapshot values = new();
    private IcomDirectLanMeterSettings config = new();

    internal IcomDirectLanMeterBar()
    {
      DoubleBuffered = true;
      Size = new Size(290, 28);
      Font = new Font("Segoe UI", 8.2f);
      Margin = Padding.Empty;
    }

    internal void UpdateMeters(
      IcomMeterSnapshot snapshot, IcomDirectLanMeterSettings settings)
    {
      values = snapshot;
      config = settings;
      Invalidate();
    }

    internal string StatusText
    {
      get
      {
        DateTime now = DateTime.UtcNow;
        var lines = new List<string>();
        foreach (var meter in EnumerateMeters())
        {
          string detail = meter.sample.HasValue && meter.sample.Value.Fresh(now)
            ? IcomMeterCivCodec.Display(meter.kind, meter.sample.Value.Raw) +
              $" (CI-V raw {meter.sample.Value.Raw:000})"
            : "No fresh CI-V readback";
          lines.Add(meter.name + ": " + detail +
            (meter.name == "PWR↑" ? " (radio PO peak held up to 1.5 seconds)" : ""));
        }
        return string.Join("\n", lines);
      }
    }

    private IEnumerable<(string name, IcomMeterKind kind, IcomMeterSample? sample)>
      EnumerateMeters()
    {
      if (config.S) yield return ("S", IcomMeterKind.S, values.S);
      if (config.Power)
      {
        var (sample, peakHeld) = values.DisplayPower(DateTime.UtcNow);
        yield return (peakHeld ? "PWR↑" : "PWR", IcomMeterKind.Power, sample);
      }
      if (config.Alc) yield return ("ALC", IcomMeterKind.Alc, values.Alc);
      if (config.Swr) yield return ("SWR", IcomMeterKind.Swr, values.Swr);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      base.OnPaint(e);
      var meters = EnumerateMeters().ToArray();
      if (meters.Length == 0) return;
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

      float cellWidth = ClientSize.Width / (float)meters.Length;
      DateTime now = DateTime.UtcNow;
      using var dim = new SolidBrush(Color.FromArgb(90, ForeColor));
      using var ink = new SolidBrush(ForeColor);
      using var level = new SolidBrush(Color.FromArgb(45, 165, 102));
      using var alarm = new SolidBrush(Color.FromArgb(219, 97, 79));
      using var background = new SolidBrush(Color.FromArgb(45, ForeColor));
      using var separator = new Pen(Color.FromArgb(60, ForeColor), 1f);

      for (int i = 0; i < meters.Length; i++)
      {
        var entry = meters[i];
        bool fresh = entry.sample is { } measure && measure.Fresh(now);
        int raw = fresh ? entry.sample.GetValueOrDefault().Raw : 0;
        string value = fresh
          ? IcomMeterCivCodec.Display(entry.kind, raw)
          : "--";
        float x = i * cellWidth + 3;
        float width = cellWidth - 8;

        e.Graphics.DrawString(entry.name, Font, ink, x, 1);
        using (var format = new StringFormat { Alignment = StringAlignment.Far })
          e.Graphics.DrawString(value, Font, fresh ? ink : dim,
            new RectangleF(x, 1, width, 15), format);

        var track = new RectangleF(x, 19, width, 5);
        e.Graphics.FillRectangle(background, track);
        float fraction = entry.kind switch
        {
          IcomMeterKind.S => raw / 241f,
          IcomMeterKind.Power =>
            (float)IcomMeterCivCodec.PowerPercent(raw) / 100,
          IcomMeterKind.Alc => raw / 120f,
          IcomMeterKind.Swr => raw / 120f,
          _ => 0
        };
        float fill = Math.Clamp(fraction, 0f, 1f);
        if (fresh && fill > 0)
        {
          bool warning = entry.kind == IcomMeterKind.Swr &&
            IcomMeterCivCodec.SwrRatio(raw) >= 3;
          e.Graphics.FillRectangle(warning ? alarm : level,
            x, 19, width * fill, 5);
        }
        if (i > 0)
          e.Graphics.DrawLine(separator, x - 5, 2, x - 5, 25);
      }
    }
  }
}
