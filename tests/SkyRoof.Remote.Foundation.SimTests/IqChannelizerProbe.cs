using SkyRoof.Remote.Foundation.Signals;

internal static class IqChannelizerProbe
{
    internal static void Run()
    {
        const int rate = 250000;
        const double center = 40000, desired = 4000;
        float[] tone = Tone(rate, center + desired);
        var whole = new IqChannelizer(rate, center).Process(tone);
        var chunked = new List<float>();
        var converter = new IqChannelizer(rate, center);
        for (int offset = 0; offset < tone.Length; offset += 2046)
            chunked.AddRange(converter.Process(tone.AsSpan(offset, Math.Min(2046, tone.Length - offset))));
        if (!whole.SequenceEqual(chunked) || whole.Length != 96000)
            throw new InvalidDataException("Rational IQ channelizer loses timing or state at packet boundaries.");
        var passed = Measure(whole, desired);
        var rejected = Measure(new IqChannelizer(rate, center).Process(Tone(rate, center + desired + 48000)), desired);
        if (passed < 0.98 || passed > 1.02 || rejected > 0.001)
            throw new InvalidDataException($"IQ passband/alias failure: wanted={passed}, alias={rejected}");
        Console.WriteLine($"PASS: rational 250k->48k CF32, exact chunk continuity, desired amplitude {passed:F6}, alias rejection {20 * Math.Log10(rejected / passed):F1} dB.");
    }

    private static float[] Tone(int rate, double frequency)
    {
        var data = new float[2 * rate];
        for (int i = 0; i < rate; i++)
        {
            var (sin, cos) = Math.SinCos(2 * Math.PI * frequency * i / rate);
            data[2 * i] = (float)cos;
            data[2 * i + 1] = (float)sin;
        }
        return data;
    }

    private static double Measure(float[] data, double frequency)
    {
        double re = 0, im = 0;
        int count = data.Length / 2;
        for (int i = 1000; i < count; i++)
        {
            var (sin, cos) = Math.SinCos(-2 * Math.PI * frequency * i / 48000);
            re += data[2 * i] * cos - data[2 * i + 1] * sin;
            im += data[2 * i] * sin + data[2 * i + 1] * cos;
        }
        return Math.Sqrt(re * re + im * im) / (count - 1000);
    }
}
