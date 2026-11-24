using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.Media;

/// <summary>
/// Stateful windowed-sinc conversion of negotiated Icom mono audio to 48 kHz.
/// Carries filter phase across UDP packets; the owner resets it on sample gaps.
/// </summary>
public sealed class IcomLanAudioResampler(int outputRate = 48_000)
{
    public const int OutputRate = 48_000;
    private const int Radius = 12;
    private readonly List<float> _samples = [];
    private long _bufferStart;
    private long _total;
    private double _nextPosition;

    public void Reset()
    {
        _samples.Clear();
        _bufferStart = 0;
        _total = 0;
        _nextPosition = 0;
    }

    public float[] Process(ReadOnlySpan<byte> pcm16, int sourceRate)
    {
        if (sourceRate is < 8_000 or > 192_000 ||
            pcm16.Length % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRate));
        var input = new float[pcm16.Length / 2];
        for (int i = 0; i < input.Length; i++)
            input[i] = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16.Slice(i * 2, 2)) / 32768f;
        if (sourceRate == outputRate) return input;

        _samples.AddRange(input);
        _total += input.Length;
        double ratio = (double)sourceRate / outputRate;
        double cutoff = Math.Min(1, (double)outputRate / sourceRate);
        var output = new List<float>((int)Math.Ceiling(input.Length / ratio) + 1);
        while (_nextPosition + Radius < _total)
        {
            long center = (long)Math.Floor(_nextPosition);
            double weighted = 0, weightSum = 0;
            for (int tap = -Radius + 1; tap <= Radius; tap++)
            {
                long index = center + tap;
                if (index < 0) continue;
                double distance = index - _nextPosition;
                double windowPosition = Math.Abs(distance) / Radius;
                if (windowPosition >= 1) continue;
                double argument = Math.PI * distance * cutoff;
                double sinc = Math.Abs(argument) < 1e-10
                    ? 1 : Math.Sin(argument) / argument;
                double window = 0.5 * (1 + Math.Cos(Math.PI * windowPosition));
                double weight = cutoff * sinc * window;
                weighted += _samples[checked((int)(index - _bufferStart))] * weight;
                weightSum += weight;
            }
            output.Add((float)(weighted / weightSum));
            _nextPosition += ratio;
        }
        long keepFrom = Math.Max(0, (long)Math.Floor(_nextPosition) - Radius);
        int trim = (int)Math.Min(_samples.Count,
            Math.Max(0, keepFrom - _bufferStart));
        if (trim > 0)
        {
            _samples.RemoveRange(0, trim);
            _bufferStart += trim;
        }
        return output.ToArray();
    }
}
