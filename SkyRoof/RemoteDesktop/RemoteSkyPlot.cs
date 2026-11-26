using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed class RemoteSkyPlot : Control
{
    private PassWindow? window;
    internal RemoteSkyPlot() { DoubleBuffered = true; BackColor = SystemColors.Window; ForeColor = SystemColors.WindowText; }

    internal void ShowPath(PassWindow? pass) { window = pass; Invalidate(); }

    internal static PointF Project(double azimuth, double elevation, PointF center, float radius)
    {
        double az = azimuth * Math.PI / 180;
        double distance = (90 - Math.Clamp(elevation, 0, 90)) / 90 * radius;
        return new PointF(center.X + (float)(Math.Sin(az) * distance), center.Y - (float)(Math.Cos(az) * distance));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
        float radius = Math.Min(center.X, center.Y) - 28;
        if (radius < 20) return;
        using var grid = new Pen(SystemColors.ControlDark);
        using var text = new SolidBrush(ForeColor);
        using var trace = new Pen(Color.DodgerBlue, 2);
        using var endpoint = new SolidBrush(Color.DodgerBlue);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (int el in new[] { 0, 30, 60 })
        {
            float r = radius * (90 - el) / 90;
            e.Graphics.DrawEllipse(grid, center.X - r, center.Y - r, 2 * r, 2 * r);
        }
        e.Graphics.DrawString("N", Font, text, center.X - 5, center.Y - radius - 20);
        e.Graphics.DrawString("S", Font, text, center.X - 5, center.Y + radius);
        e.Graphics.DrawString("W", Font, text, center.X - radius - 20, center.Y - 8);
        e.Graphics.DrawString("E", Font, text, center.X + radius + 5, center.Y - 8);
        if (window is not { } selected || selected.SkyPath.Count < 2) return;
        var points = selected.SkyPath.Select(p => Project(p.AzimuthDegrees, p.ElevationDegrees, center, radius)).ToArray();
        e.Graphics.DrawLines(trace, points);
        e.Graphics.FillEllipse(endpoint, points[0].X - 4, points[0].Y - 4, 8, 8);
        e.Graphics.DrawString("AOS", Font, text, points[0]);
        e.Graphics.DrawString("LOS", Font, text, points[^1]);
    }
}
