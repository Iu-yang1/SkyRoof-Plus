using System.Buffers.Binary;

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

      // The AMSAT-EA native x86 packet structures contain little-endian
      // 32-bit sclock values although each *byte* is transmitted MSB-first.
      // It is an onboard counter, not an absolute UTC timestamp.
      // Packet types 6/8/9 do not begin with this clock.
      if (type is 1 or 2 or 3 or 4 or 5 or 10 or 12 or 14 &&
          bytes.Length >= 5)
        yield return ("Satellite clock (seconds since boot)",
          ReadU32(bytes, 1).ToString());

      if (type == 1 && bytes.Length >= 29)
      {
        // Direct parity with AMSAT-EA's unne1b.dll v1.14 (Bytes)
        // visualiza_powerpacket / GENESIS power_packet:
        // power values are 2 mW/LSB, 16-bit members are native little-
        // endian, and 12-bit fields are split across those members.
        string[] powerNames = { "Panel A power", "Panel B power",
          "Panel C power", "Panel D power" };
        for (int i = 0; i < powerNames.Length; i++)
          yield return (powerNames[i], bytes[5 + i] * 2 + " mW");
        yield return ("Instant panel power", ReadU16(bytes, 9) * 2 + " mW");

        uint vbusVbat = ReadU16(bytes, 11);
        uint vbatVcpu = ReadU16(bytes, 13);
        uint vcpuVbus2 = ReadU16(bytes, 15);
        uint vbus3Vbat2 = ReadU16(bytes, 17);
        uint vbat2Ibat = ReadU16(bytes, 19);
        uint ibatIcpu = ReadU16(bytes, 21);
        uint icpuIpl = ReadU16(bytes, 23);

        uint vbus1 = (vbusVbat >> 4) * 1400 / 1000;
        uint vbat1Raw = ((vbusVbat << 8) & 0x0F00) |
          ((vbatVcpu >> 8) & 0xFF);
        uint vbat1 = vbat1Raw * 1400 / 1000;
        uint vbus2 = (vcpuVbus2 & 0x0FFF) * 4;
        uint vbus3 = (vbus3Vbat2 >> 4) * 4;
        uint vbat2Raw = ((vbus3Vbat2 << 8) & 0x0F00) |
          (vbat2Ibat >> 8);
        uint vbat2 = vbat2Raw * 4;

        yield return ("VBUS1", vbus1 + " mV");
        yield return ("VBAT1", vbat1 + " mV");
        yield return ("VBUS2", vbus2 + " mV");
        yield return ("VBUS3", vbus3 + " mV");
        yield return ("VBAT2", vbat2 + " mV");

        uint vcpuRaw = ((vbatVcpu << 4) & 0x0FF0) |
          (vcpuVbus2 >> 12);
        if (vcpuRaw != 0)
          yield return ("VCPU", 1210u * 4096 / vcpuRaw + " mV");

        int ibat = Signed12(((vbat2Ibat << 8) & 0x0F00) |
          (ibatIcpu >> 8));
        int icpuRaw = Signed12(((ibatIcpu << 4) & 0x0FF0) |
          (icpuIpl >> 12));
        // AMSAT-EA documents the CPU-current sensor sign as reversed.
        int icpu = icpuRaw < 0 ? -icpuRaw : icpuRaw;
        int ipl = Signed12(icpuIpl & 0x0FFF);
        yield return ("IBAT", ibat + " mA");
        yield return ("ICPU", icpu + " mA");
        yield return ("Payload current", ipl + " mA");

        string[] levelNames = { "Peak signal", "Mean noise",
          "Last command signal", "Last command noise" };
        for (int i = 0; i < levelNames.Length; i++)
          yield return (levelNames[i],
            (bytes[25 + i] * 0.5).ToString("0.0") +
            (i < 2 ? " dBm" : " dB"));
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
        yield return ("Uptime", ReadU32(bytes, 5) + " s");
        yield return ("CPU runs", ReadU16(bytes, 9).ToString());
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

    private static ushort ReadU16(byte[] data, int offset) =>
      BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static uint ReadU32(byte[] data, int offset) =>
      BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static int Signed12(uint value)
    {
      int raw = (int)(value & 0x0FFF);
      return (raw & 0x0800) != 0 ? raw - 0x1000 : raw;
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
