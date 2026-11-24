using System.Buffers.Binary;

internal static partial class CwLiveProbe
{
    private static byte[] MakeWaveform(int seconds)
    {
        const int rate = 48000;
        var first = KeyPattern(["...", "---", "..."]);
        var second = KeyPattern(["-.-.", "--.-"]);
        var data = new byte[rate * seconds * 2];
        var random = new Random(1234567);
        for (int i = 0; i < data.Length / 2; i++)
        {
            double time = i / (double)rate;
            double signal = 0.004 * (random.NextDouble() * 2 - 1);
            if (first[(int)(time / 0.060) % first.Length])
                signal += 0.30 * Math.Sin(2 * Math.PI * 800 * time);
            if (second[(int)((time + 0.12) / 0.075) % second.Length])
                signal += 0.22 * Math.Sin(2 * Math.PI * 1200 * time);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), (short)Math.Round(signal * 32767));
        }
        return data;
    }

    private static bool[] KeyPattern(string[] letters)
    {
        var result = new List<bool>();
        foreach (string letter in letters)
        {
            for (int j = 0; j < letter.Length; j++)
            {
                result.AddRange(Enumerable.Repeat(true, letter[j] == '-' ? 3 : 1));
                if (j + 1 < letter.Length) result.Add(false);
            }
            result.AddRange(Enumerable.Repeat(false, 3));
        }
        result.AddRange(Enumerable.Repeat(false, 4));
        return result.ToArray();
    }
}
