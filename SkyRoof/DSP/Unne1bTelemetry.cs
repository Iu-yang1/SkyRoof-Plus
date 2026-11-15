namespace SkyRoof
{
  /// <summary>
  /// UNNE-1B engineering values defined in AMSAT-EA Transmissions Overview
  /// V1.04 (2026-02-11). Input is already CRC-verified and descrambled.
  /// Retain raw units when the published document gives no calibration.
  /// </summary>
  internal static class Unne1bTelemetry
  {
    private static readonly string[] TempNames =
      { "Panel A", "Panel B", "Panel C", "Panel D", "Panel E",
        "EPS", "TX", "TX2", "RX", "CPU" };

    internal static IEnumerable<(string Name, string Value)> Describe(byte[] bytes)
    {
      if (bytes.Length == 0 || (bytes[0] & 0x0F) != 0x0C) yield break;
      int type = bytes[0] >> 4;
      yield return ("Satellite", "UNNE-1B");
      yield return ("Packet type", type.ToString());

      // Packet types 6/8/9 do not begin with a 32-bit clock; do not
      // mislabel their first sensor bits as a satellite time value.
      if (type is 1 or 2 or 3 or 4 or 5 or 10 or 12 or 14 &&
          bytes.Length >= 5)
        yield return ("Satellite clock / UTC raw", ReadBits(bytes, 8, 32).ToString());

      if (type == 1 && bytes.Length >= 29)
      {
        string[] powerNames = { "Panel A power", "Panel B power",
          "Panel C power", "Panel D power" };
        for (int i = 0; i < powerNames.Length; i++)
          yield return (powerNames[i], bytes[5 + i] + " mW");
        yield return ("Instant panel power", ReadBits(bytes, 72, 16) + " mW");

        string[] analogNames = { "VBUS1", "VBAT1", "VCPU", "VBUS2",
          "VBUS3", "VBAT2", "IBAT", "ICPU", "Payload current" };
        int[] widths = { 12, 12, 12, 16, 12, 12, 12, 12, 12 };
        int offset = 88;
        for (int i = 0; i < analogNames.Length; i++)
        {
          uint raw = ReadBits(bytes, offset, widths[i]);
          yield return (analogNames[i], raw +
            (i < 6 ? " mV" : " mA"));
          offset += widths[i];
        }
        string[] levels = { "Peak signal", "Mean noise", "Last command signal",
          "Last command noise" };
        for (int i = 0; i < levels.Length; i++)
          yield return (levels[i], (ReadBits(bytes, offset + i * 8, 8) * 0.5)
            .ToString("0.0") + " dB");
      }

      if (type is 2 or 5)
      {
        if (type == 2 && bytes.Length >= 15)
          for (int i = 0; i < 10; i++)
            yield return ("Temperature " + TempNames[i], Temperature(bytes[5 + i]));
        if (type == 5 && bytes.Length >= 25)
          for (int i = 0; i < 10; i++)
          {
            yield return ("Minimum " + TempNames[i], Temperature(bytes[5 + i]));
            yield return ("Maximum " + TempNames[i], Temperature(bytes[15 + i]));
          }
      }

      if (type == 3 && bytes.Length >= 27)
      {
        yield return ("Uptime", ReadBits(bytes, 40, 32) + " s");
        yield return ("CPU runs", ReadBits(bytes, 72, 16).ToString());
        yield return ("Payload starts", bytes[11].ToString());
        yield return ("Antenna deploy attempts", bytes[12].ToString());
        yield return ("Repeater starts", bytes[13].ToString());
        yield return ("Payload failures", (bytes[14] >> 4).ToString());
        yield return ("Last reset reason", (bytes[14] & 15).ToString());
        int mode = bytes[15] & 15;
        yield return ("Transponder mode", mode switch
        {
          0 => "OFF",
          1 => "FM->FM",
          2 => "FSK->FSK regenerative",
          _ => mode.ToString()
        });
        yield return ("Antenna deployment status", bytes[17].ToString());
      }

      if (type == 6 && bytes.Length >= 133)
      {
        yield return ("Sun sensor intervals",
          Convert.ToHexString(bytes.AsSpan(1, 12)));
        yield return ("Sun sensor readings", "48 x 16-bit raw samples");
        yield return ("Sun sensor peak values", "8 x 16-bit raw values");
        yield return ("Sun sensor status",
          Convert.ToHexString(bytes.AsSpan(125, 8)));
      }

      if (type == 8 && bytes.Length >= 29)
      {
        string[] names = { "V1 open circuit", "V1", "I1", "I1 peak",
          "R1", "V2 open circuit", "V2", "R2" };
        for (int i = 0; i < names.Length; i++)
          yield return (names[i], ReadBits(bytes, 8 + i * 16, 16).ToString());
        yield return ("Deployment time", ReadBits(bytes, 136, 32).ToString());
        yield return ("Deployment states", Convert.ToHexString(bytes.AsSpan(23, 6)));
      }

      if (type == 9 && bytes.Length >= 121)
      {
        string[] channelNames = { "SPA", "SPB", "SPC", "SPD", "BAT",
          "BUS", "BATP", "BATN", "CPU", "Payload" };
        for (int i = 0; i < channelNames.Length; i++)
        {
          int start = 8 + i * 96;
          yield return (channelNames[i] + " instant V",
            ReadBits(bytes, start, 16).ToString() + " raw");
          yield return (channelNames[i] + " instant I",
            ReadBits(bytes, start + 16, 16).ToString() + " raw");
          yield return (channelNames[i] + " mean P",
            ReadBits(bytes, start + 32, 16).ToString() + " raw");
        }
      }

      if (type == 10 && bytes.Length >= 15)
      {
        yield return ("Game week", bytes[5].ToString());
        yield return ("Game status", bytes[6].ToString());
        yield return ("Game data (8 bytes)", Convert.ToHexString(bytes.AsSpan(7, 8)));
      }

      if (type == 12 && bytes.Length >= 62)
      {
        yield return ("Spacecraft address", ReadBits(bytes, 40, 16).ToString());
        yield return ("TLE epoch (raw)", ReadBits(bytes, 120, 32).ToString());
        yield return ("Latitude (raw)", ReadBits(bytes, 440, 16).ToString());
        yield return ("Longitude (raw)", ReadBits(bytes, 456, 16).ToString());
        yield return ("Altitude (raw)", ReadBits(bytes, 472, 16).ToString());
      }

      if (type == 14 && bytes.Length >= 36)
      {
        byte variable = bytes[5];
        string variableName = variable switch
        {
          0 => "Peak signal",
          1 => "Mean noise",
          2 => "VBAT1",
          3 => "CPU temperature",
          4 => "Panel A temperature",
          5 => "Panel average temperature",
          _ => variable.ToString()
        };
        yield return ("Time series variable", variableName);
        byte[] samples = bytes.AsSpan(6, 30).ToArray();
        yield return ("Time series", "30 samples at 3-minute interval");
        yield return ("First / latest samples", samples[0] + " / " + samples[^1]);
        yield return ("Minimum / maximum raw", samples.Min() + " / " + samples.Max());
      }

      if (type == 15 && bytes.Length >= 2)
        yield return ("CODEC2 subframe index", bytes[1].ToString());
    }

    private static uint ReadBits(byte[] data, int offset, int width)
    {
      uint value = 0;
      for (int i = 0; i < width; i++)
      {
        int position = offset + i;
        value = (value << 1) | (uint)((data[position / 8] >>
          (7 - position % 8)) & 1);
      }
      return value;
    }

    private static string Temperature(byte value) => value switch
    {
      0 => "<= -40 °C",
      254 => ">= 87 °C",
      255 => "Sensor error",
      _ => (value * 0.5 - 40).ToString("0.0") + " °C"
    };
  }
}
