using System.Buffers.Binary;
using System.Numerics;

namespace SkyRoof.Remote.Dsp;

/// <summary>
/// 48 kHz PCM16 AF-only analyzer, 4096-point Hann FFT, 1600-sample hop.
/// It does not represent IC-9700 RF spectrum, IF passband or tuned frequency.
/// </summary>
public sealed class AudioSpectrumTransform
{
    public const int SampleRate = 48000;
    public const int FftLength = 4096;
    public const int BinCount = 475;
    public const int HopSize = 1600;
    private readonly float[] _samples = new float[FftLength];
    private readonly double[] _window = Enumerable.Range(0, FftLength)
        .Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftLength - 1))).ToArray();
    private readonly double _windowSum;
    private int _write, _filled, _since;
    private ulong _expectedSampleIndex;
    private bool _started;
    private ulong _epoch;
    public ulong Epoch => _epoch;
    public ulong Discontinuities { get; private set; }

    public AudioSpectrumTransform() { _windowSum = _window.Sum(); }

    public void Reset(ulong epoch)
    {
        _epoch = epoch;
        _write = _filled = _since = 0;
        _expectedSampleIndex = 0;
        _started = false;
        Array.Clear(_samples);
    }

    public void Accept(ulong epoch, ulong firstSampleIndex, int sampleRate, int samplesPerChannel,
        int channels, ReadOnlySpan<byte> pcm, Action<ulong, float[]> onSpectrum)
    {
        if (sampleRate != SampleRate || channels is < 1 or > 2 ||
            samplesPerChannel < 1 || pcm.Length != samplesPerChannel * channels * sizeof(short))
            throw new InvalidDataException("Audio spectrum input must be 48 kHz PCM16 LE.");

        if (_epoch != epoch) Reset(epoch);
        if (_started && firstSampleIndex != _expectedSampleIndex)
        {
            Discontinuities++;
            Reset(epoch);
        }
        _started = true;
        _expectedSampleIndex = checked(firstSampleIndex + (ulong)samplesPerChannel);
        for (var i = 0; i < samplesPerChannel; ++i)
        {
            var offset = i * channels * 2;
            var left = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(offset, 2)) / 32768f;
            var mono = channels == 2
                ? (left + BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(offset + 2, 2)) / 32768f) * 0.5f
                : left;
            _samples[_write] = mono;
            _write = (_write + 1) % FftLength;
            _filled = Math.Min(_filled + 1, FftLength);
            _since++;
            if (_filled == FftLength && _since >= HopSize)
            {
                _since = 0;
                onSpectrum(firstSampleIndex + (ulong)i + 1 - FftLength, Compute());
            }
        }
    }

    private float[] Compute()
    {
        var fft = new Complex[FftLength];
        for (var i = 0; i < FftLength; i++)
            fft[i] = new Complex(_samples[(_write + i) % FftLength] * _window[i], 0);

        // In-place iterative radix-2 Cooley–Tukey.
        for (int i = 1, j = 0; i < FftLength; i++)
        {
            int bit = FftLength >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (fft[i], fft[j]) = (fft[j], fft[i]);
        }
        for (int size = 2; size <= FftLength; size <<= 1)
        {
            var twiddle = Complex.FromPolarCoordinates(1, -2 * Math.PI / size);
            for (var start = 0; start < FftLength; start += size)
            {
                var phase = Complex.One;
                for (var j = 0; j < size / 2; j++)
                {
                    var lower = fft[start + j + size / 2] * phase;
                    var upper = fft[start + j];
                    fft[start + j] = upper + lower;
                    fft[start + j + size / 2] = upper - lower;
                    phase *= twiddle;
                }
            }
        }
        var output = new float[BinCount];
        for (var i = 0; i < BinCount; ++i)
        {
            // 475 display bins are wider than native FFT bins. Averaging dB
            // values at their centers attenuates narrow CW tones by >10 dB.
            // Preserve peak amplitude by pooling FFT power inside each display
            // frequency cell; convert the peak to dBFS only once.
            var center = i * (FftLength / 2.0) / (BinCount - 1);
            var width = (FftLength / 2.0) / (BinCount - 1);
            var first = Math.Max(0, (int)Math.Ceiling(center - width / 2));
            var last = Math.Min(FftLength / 2, (int)Math.Floor(center + width / 2));
            var peak = 0.0;
            for (var j = first; j <= last; ++j)
                peak = Math.Max(peak, fft[j].Magnitude * (j is 0 or FftLength / 2 ? 1 : 2)
                    / _windowSum);
            output[i] = (float)Math.Clamp(20 * Math.Log10(Math.Max(1e-6, peak)), -120, 0);
        }
        return output;
    }
}
