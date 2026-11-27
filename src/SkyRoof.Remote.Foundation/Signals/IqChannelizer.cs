namespace SkyRoof.Remote.Foundation.Signals;

/// <summary>Continuous complex mixer and fractional-delay FIR resampler to 48 kHz.</summary>
public sealed class IqChannelizer
{
    public const int OutputRate = 48000;
    private const int PhaseCount = 128;
    private readonly int _inputRate, _length;
    private readonly float[][] _phases;
    private readonly float[] _real, _imaginary;
    private readonly double _stepRe, _stepIm;
    private double _oscRe = 1, _oscIm;
    private long _inputIndex, _outputTime;
    private int _head;

    public IqChannelizer(int inputRate, double offsetHz)
    {
        if (inputRate < OutputRate || !double.IsFinite(offsetHz) || Math.Abs(offsetHz) + 18000 > inputRate / 2.0)
            throw new ArgumentException("Input must contain the selected 36 kHz complex passband.");
        _inputRate = inputRate;
        // Blackman-windowed sinc: 18 kHz passband, 24 kHz stopband, 21 kHz midpoint.
        _length = checked(((int)Math.Ceiling(5.5 * inputRate / 6000.0) | 1));
        _phases = CreatePhases(inputRate, _length);
        _real = new float[checked(2 * _length)];
        _imaginary = new float[_real.Length];
        (_stepIm, _stepRe) = Math.SinCos(-2 * Math.PI * offsetHz / inputRate);
    }

    public double GroupDelaySeconds => (_length / 2) / (double)_inputRate;

    public float[] Process(ReadOnlySpan<float> cf32)
    {
        if ((cf32.Length & 1) != 0) throw new ArgumentException("CF32 input must have I/Q pairs.");
        int maximum = checked((int)((long)(cf32.Length / 2) * OutputRate / _inputRate + 2) * 2);
        var output = new float[maximum];
        int written = 0;
        while (!cf32.IsEmpty)
        {
            float i = cf32[0], q = cf32[1];
            cf32 = cf32[2..];
            Push(i, q);
            long boundary = checked((_inputIndex + 1) * OutputRate);
            while (_outputTime < boundary)
            {
                int phase = (int)((_outputTime % OutputRate) * PhaseCount / OutputRate);
                var coefficients = _phases[phase];
                var (re, im) = Convolve(coefficients);
                output[written++] = re;
                output[written++] = im;
                _outputTime += _inputRate;
            }
            _inputIndex++;
        }
        Array.Resize(ref output, written);
        return output;
    }

    private void Push(float i, float q)
    {
        _head = (_head + 1) % _length;
        float re = (float)(i * _oscRe - q * _oscIm);
        float im = (float)(i * _oscIm + q * _oscRe);
        _real[_head] = _real[_head + _length] = re;
        _imaginary[_head] = _imaginary[_head + _length] = im;
        (_oscRe, _oscIm) = (_oscRe * _stepRe - _oscIm * _stepIm,
            _oscRe * _stepIm + _oscIm * _stepRe);
        if ((_inputIndex & 4095) == 4095)
        {
            double magnitude = Math.Sqrt(_oscRe * _oscRe + _oscIm * _oscIm);
            _oscRe /= magnitude;
            _oscIm /= magnitude;
        }
    }

    private (float Re, float Im) Convolve(float[] coefficients)
    {
        float re = 0, im = 0;
        int sample = _head + _length;
        foreach (float coefficient in coefficients)
        {
            re += _real[sample] * coefficient;
            im += _imaginary[sample--] * coefficient;
        }
        return (re, im);
    }

    private static float[][] CreatePhases(int rate, int length)
    {
        var phases = new float[PhaseCount][];
        double cutoff = 21000.0 / rate;
        for (int phase = 0; phase < PhaseCount; phase++)
        {
            var coefficients = new float[length];
            double sum = 0;
            int k = 0;
            foreach (ref float value in coefficients.AsSpan())
            {
                double t = length / 2 - k - (double)phase / PhaseCount;
                double sinc = t == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * t) / (Math.PI * t);
                double w = 2 * Math.PI * k++ / (length - 1);
                value = (float)(sinc * (0.42 - 0.5 * Math.Cos(w) + 0.08 * Math.Cos(2 * w)));
                sum += value;
            }
            foreach (ref float value in coefficients.AsSpan()) value = (float)(value / sum);
            phases[phase] = coefficients;
        }
        return phases;
    }
}
