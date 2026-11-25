using VE3NEA.SkyTlm.Audio;
using VE3NEA.SkyTlm.Core;
using VE3NEA.SkyTlm.Imaging;

namespace SkyRoof.Remote.Dsp;

internal sealed record TelemetryMediaProduct(string Extension, byte[] Bytes, string Label,
    int Received, int Expected, bool Complete);

internal sealed class TelemetryMediaAssembler : IDisposable
{
    private readonly IImageAssembler? _image;
    private IAudioAssembler? _voice;

    internal TelemetryMediaAssembler(SignalParams signal, int norad, Action<TelemetryMediaProduct> publish)
    {
        // The current HADES media factories implement HADES-SA, not UNNE or other GENESIS variants.
        if (signal.Framing == Framing.HADES && norad != 68446) return;
        _image = ImageAssemblerFactory.Create(signal, norad);
        if (_image is not null)
            _image.ImageCompleted += product =>
            {
                if (product.Jpeg.Length > 0)
                    publish(new("jpg", product.Jpeg, $"Image {product.ImageId} · {product.Width}×{product.Height}",
                        product.FragmentsReceived, product.FragmentsExpected, product.Complete));
                else if (product.Text is { } text)
                    publish(new("txt", System.Text.Encoding.UTF8.GetBytes(text), $"Text {product.ImageId}",
                        product.FragmentsReceived, product.FragmentsExpected, product.Complete));
            };
        try
        {
            _voice = AudioAssemblerFactory.Create(signal, norad);
            if (_voice is not null)
                _voice.VoiceCompleted += product => publish(new("wav", product.Wav,
                    $"Codec2 · {product.DurationSeconds:F1}s (received span, no per-fragment CRC)",
                    product.SubFramesReceived, product.SubFramesExpected, product.Complete));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
            BadImageFormatException or InvalidOperationException)
        {
            Serilog.Log.Warning("Telemetry voice assembler unavailable ({Type})", error.GetType().Name);
        }
    }

    internal void Push(Frame frame)
    {
        _image?.Push(frame);
        _voice?.Push(frame);
    }

    internal void Flush()
    {
        _image?.Flush();
        _voice?.Flush();
    }

    public void Dispose()
    {
        Flush();
        (_voice as IDisposable)?.Dispose();
        _voice = null;
    }
}
