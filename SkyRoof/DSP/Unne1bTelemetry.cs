namespace SkyRoof
{
  /// <summary>Operator-facing, bit-field accurate UNNE-1B engineering summaries.
  /// All other packet bytes remain available in the existing HEX view.</summary>
  internal static class Unne1bTelemetry
  {
    internal static IEnumerable<(string Name, string Value)> Describe(byte[] bytes)
    {
      if (bytes.Length == 0 || (bytes[0] & 0x0F) != 0x0C) yield break;
      int type = bytes[0] >> 4;
      yield return ("Satellite", "UNNE-1B");
      yield return ("Packet type", type.ToString());
      if (type is 1 or 2 or 3 or 4 or 5 or 10 or 12 or 14 && bytes.Length >= 5)
      {
        uint clock = ((uint)bytes[1] << 24) | ((uint)bytes[2] << 16) |
                     ((uint)bytes[3] << 8) | bytes[4];
        yield return ("Satellite clock", clock + " s");
      }
      if (type == 2 && bytes.Length >= 15)
      {
        string[] labels = { "Panel A", "Panel B", "Panel C", "Panel D",
          "Panel E", "EPS", "TX", "TX2", "RX", "CPU" };
        for (int i = 0; i < 10; i++)
        {
          byte v = bytes[5 + i];
          string value = v == 255 ? "Sensor error" :
            v == 0 ? "<= -40 °C" : v >= 254 ? ">= 87 °C" :
            (v * 0.5 - 40).ToString("0.0") + " °C";
          yield return ("Temperature " + labels[i], value);
        }
      }
      if (type == 10 && bytes.Length >= 15)
      {
        yield return ("Game week", bytes[5].ToString());
        yield return ("Game status", bytes[6].ToString());
        yield return ("Game data (8 bytes)", Convert.ToHexString(bytes.AsSpan(7, 8)));
      }
      if (type == 15 && bytes.Length >= 2)
        yield return ("CODEC2 subframe index", bytes[1].ToString());
    }
  }
}
