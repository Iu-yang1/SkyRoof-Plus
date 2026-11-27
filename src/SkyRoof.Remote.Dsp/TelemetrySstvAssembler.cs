using System.Drawing.Imaging;
using MathNet.Numerics;
using VE3NEA.SkySSTV;

namespace SkyRoof.Remote.Dsp;

internal sealed class TelemetrySstvAssembler : IDisposable
{
    private readonly SstvDecoder _decoder = new();
    private readonly TelemetryAfskFmIqConverter _audio = new(3300);
    private bool _flushed;
    private readonly Dictionary<int, long> _lastPreview = new();

    internal TelemetrySstvAssembler(Action<TelemetryMediaProduct> publish)
    {
        _decoder.ImageCompleted += image => PublishImage(image, publish);
        _decoder.ImageUpdated += image => PublishImage(image, publish);
    }

    private void PublishImage(SstvImageEvent image, Action<TelemetryMediaProduct> publish)
    {
            if (image.ValidRows <= 0) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (image.Final) _lastPreview.Remove(image.ImageId);
            else
            {
                if (_lastPreview.TryGetValue(image.ImageId, out long last) &&
                    System.Diagnostics.Stopwatch.GetElapsedTime(last, now) < TimeSpan.FromSeconds(1)) return;
                _lastPreview[image.ImageId] = now;
            }
            using var bitmap = image.Image.ToBitmap();
            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            int received = image.Planes?.RowRendered.Count(row => row) ?? image.ValidRows;
            publish(new("png", output.ToArray(), $"SSTV {image.Mode} · {image.Image.Width}×{image.Image.Height}",
                received, image.Image.Height, image.Final && received == image.Image.Height,
                "sstv:" + image.ImageId, image.Final));
    }

    internal void PushAudio(ReadOnlySpan<float> samples) => PushIq(_audio.Convert(samples));
    internal void PushIq(ReadOnlySpan<Complex32> samples) => _decoder.Process(samples);

    internal void Flush()
    {
        if (_flushed) return;
        _flushed = true;
        _decoder.Flush();
    }

    public void Dispose()
    {
        Flush();
        _decoder.Dispose();
    }
}
