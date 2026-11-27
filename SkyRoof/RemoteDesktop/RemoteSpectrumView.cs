using System.Buffers.Binary;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed class RemoteSpectrumView : Control
{
    private float[] values = [];
    private string caption = "No spectrum";
    private float low = -120, high = 0;

    internal RemoteSpectrumView()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(21, 27, 35);
        ForeColor = Color.FromArgb(200, 220, 240);
    }

    internal void Accept(StationSignalKind kind, StationSignalFrame frame)
    {
        var data = frame.Payload.Span;
        if (kind == StationSignalKind.StationSignalAfSpectrum)
        {
            if (data.Length != 475 * 4 || frame.SampleCount != 475) return;
            values = new float[475];
            for (int i = 0; i < values.Length; i++)
                values[i] = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(i * 4, 4));
            low = -120; high = 0; caption = "AF spectrum · dB (not calibrated RF dBm)";
        }
        else
        {
            if (data.Length != 499 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x31465253) return;
            values = data[24..].ToArray().Select(x => (float)x).ToArray();
            low = 0; high = 160;
            caption = $"RF scope · {BinaryPrimitives.ReadInt64LittleEndian(data[8..]):N0} – {BinaryPrimitives.ReadInt64LittleEndian(data[16..]):N0} Hz";
            if (data[6] != 0) caption += " · OUT OF RANGE";
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var text = new SolidBrush(ForeColor);
        e.Graphics.DrawString(caption, Font, text, 8, 6);
        if (values.Length < 2 || ClientSize.Height < 40) return;
        float w = Math.Max(1, ClientSize.Width - 16), h = ClientSize.Height - 35;
        var points = new PointF[values.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float y = float.IsFinite(values[i]) ? Math.Clamp(values[i], low, high) : low;
            points[i] = new PointF(8 + w * i / (points.Length - 1), 26 + h * (high - y) / (high - low));
        }
        using var trace = new Pen(Color.FromArgb(85, 200, 220), 1.2f);
        e.Graphics.DrawLines(trace, points);
    }
}
