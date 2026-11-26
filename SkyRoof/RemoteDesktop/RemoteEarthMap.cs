using VE3NEA;
using SkyRoof.Remote.Ipc;

namespace SkyRoof.RemoteDesktop;

internal sealed class RemoteEarthMap : Control
{
    private readonly Bitmap texture = Utils.BitmapFromBytes(Properties.Resources.dxcc);
    private GroundTrackResult? track;
    private GroundTrackPoint? current;
    private double zoom = 1;
    private double centerLon, centerLat;
    private Point? drag;

    internal RemoteEarthMap() { DoubleBuffered = true; BackColor = Color.FromArgb(17, 24, 39); }

    internal void SetTrack(GroundTrackResult value, long utc, bool full)
    {
        if (full || track?.NoradNumber != value.NoradNumber || track.CelestialTarget != value.CelestialTarget)
            track = value;
        current = value.Points.MinBy(p => Math.Abs(p.UtcMilliseconds - utc));
        Invalidate();
    }

    internal static double Wrap(double longitude) => ((longitude + 180) % 360 + 360) % 360 - 180;
    private PointF Project(double lat, double lon) => new(
        ClientSize.Width / 2f + (float)(Wrap(lon - centerLon) * ClientSize.Width * zoom / 360),
        ClientSize.Height / 2f - (float)((lat - centerLat) * ClientSize.Height * zoom / 180));

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        zoom = Math.Clamp(zoom * Math.Pow(1.2, e.Delta / 120.0), 1, 12);
        Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = e.Location; Capture = true; } }
    protected override void OnMouseUp(MouseEventArgs e) { drag = null; Capture = false; }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag is not { } previous) return;
        centerLon = Wrap(centerLon - (e.X - previous.X) * 360 / (ClientSize.Width * zoom));
        centerLat = Math.Clamp(centerLat + (e.Y - previous.Y) * 180 / (ClientSize.Height * zoom), -90 + 90 / zoom, 90 - 90 / zoom);
        drag = e.Location;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
        DrawTexture(e.Graphics);
        if (track is not { } model) return;
        using var line = new Pen(Color.Cyan, 2);
        using var marker = new SolidBrush(Color.Yellow);
        using var site = new SolidBrush(Color.LimeGreen);
        PointF? previous = null;
        foreach (var point in model.Points)
        {
            var pixel = Project(point.LatitudeDegrees, point.LongitudeDegrees);
            if (previous is { } p && Math.Abs(p.X - pixel.X) < ClientSize.Width * zoom / 2)
                e.Graphics.DrawLine(line, p, pixel);
            previous = pixel;
        }
        var home = Project(model.SiteLatitudeDegrees, model.SiteLongitudeDegrees);
        e.Graphics.FillEllipse(site, home.X - 4, home.Y - 4, 8, 8);
        if (current is { } selected)
        {
            var pos = Project(selected.LatitudeDegrees, selected.LongitudeDegrees);
            e.Graphics.FillEllipse(marker, pos.X - 5, pos.Y - 5, 10, 10);
            e.Graphics.DrawString($"{selected.LatitudeDegrees:F2}°, {selected.LongitudeDegrees:F2}° · {selected.HeightMetres / 1000:F0} km", Font, marker, pos);
        }
    }

    private void DrawTexture(Graphics graphics)
    {
        float w = (float)(ClientSize.Width * zoom), h = (float)(ClientSize.Height * zoom);
        float x = ClientSize.Width / 2f - w / 2 - (float)(centerLon * w / 360);
        float y = ClientSize.Height / 2f - h / 2 + (float)(centerLat * h / 180);
        for (int offset = -1; offset <= 1; offset++)
            graphics.DrawImage(texture, new RectangleF(x + offset * w, y, w, h));
    }

    protected override void Dispose(bool disposing) { if (disposing) texture.Dispose(); base.Dispose(disposing); }
}
