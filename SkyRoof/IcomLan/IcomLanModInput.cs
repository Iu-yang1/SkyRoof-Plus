using System.Globalization;

namespace SkyRoof
{
  internal enum IcomLanModRegister : byte
  {
    LanLevel = 0x14,
    DataOffSource = 0x15,
    DataSource = 0x16
  }

  internal readonly record struct IcomLanModReading(
    IcomLanModRegister Register, int Value);

  /// <summary>
  /// IC-9700 1A 05 0114/0115/0116: LAN MOD Level and DATA OFF/DATA
  /// modulation inputs. The latter two encode LAN as BCD 05.
  /// </summary>
  internal static class IcomLanModCivCodec
  {
    internal const int LanSource = 5;

    internal static byte[] BuildRead(byte radio, IcomLanModRegister register) =>
      [0xFE, 0xFE, radio, 0xE0, 0x1A, 0x05, 0x01, (byte)register, 0xFD];

    internal static byte[] BuildSourceWrite(
      byte radio, IcomLanModRegister register, int value)
    {
      if (register is not (IcomLanModRegister.DataOffSource or
                            IcomLanModRegister.DataSource) ||
          value is < 0 or > 5)
        throw new ArgumentOutOfRangeException(nameof(value));

      return [0xFE, 0xFE, radio, 0xE0, 0x1A, 0x05, 0x01,
        (byte)register, (byte)value, 0xFD];
    }

    internal static bool TryParse(
      ReadOnlySpan<byte> frame, byte radio, out IcomLanModReading reading)
    {
      reading = default;
      if (frame.Length is not (11 or 12) ||
          frame[0] != 0xFE || frame[1] != 0xFE ||
          (frame[2] != 0xE0 && frame[2] != 0x00) ||
          frame[3] != radio ||
          frame[4] != 0x1A || frame[5] != 0x05 ||
          frame[6] != 0x01 || frame[^1] != 0xFD ||
          !Enum.IsDefined(typeof(IcomLanModRegister), frame[7]))
        return false;

      var register = (IcomLanModRegister)frame[7];
      if (register == IcomLanModRegister.LanLevel)
      {
        if (frame.Length != 12 ||
            !Bcd(frame[8]) || !Bcd(frame[9]))
          return false;
        int level = (frame[8] >> 4) * 1000 +
                    (frame[8] & 15) * 100 +
                    (frame[9] >> 4) * 10 +
                    (frame[9] & 15);
        if (level > 255) return false;
        reading = new(register, level);
        return true;
      }

      if (frame.Length != 11 || frame[8] > 5) return false;
      reading = new(register, frame[8]);
      return true;
    }

    private static bool Bcd(byte b) => (b >> 4) < 10 && (b & 15) < 10;

    internal static string DescribeSource(int? value) =>
      value switch
      {
        0 => "MIC", 1 => "ACC", 2 => "MIC/ACC",
        3 => "USB", 4 => "MIC/USB", 5 => "LAN",
        _ => "unconfirmed"
      };

    internal static string DescribeLevel(int? raw) =>
      raw is int n
        ? (n * 100.0 / 255).ToString("F0", CultureInfo.InvariantCulture) + "%"
        : "unconfirmed";
  }

  /// <summary>
  /// Stores only confirmed radio registers. Auto-selection is an explicit
  /// TX setting; each newly observed non-LAN value is corrected once, then
  /// independently checked again by CI-V readback. No PTT command is issued.
  /// </summary>
  internal sealed class IcomLanModInputState
  {
    private int? dataOff;
    private int? data;
    private int? lanLevel;
    private int dataOffWriteAttempts;
    private int dataWriteAttempts;
    private string description = "radio LAN MOD input not read";
    private int confirmedWrong;
    private int levelZero;

    internal string Description => Volatile.Read(ref description);
    internal bool ConfirmedWrongSource => Volatile.Read(ref confirmedWrong) == 1;
    internal bool ConfirmedZeroLanLevel => Volatile.Read(ref levelZero) == 1;
    internal int? DataOff => dataOff;
    internal int? Data => data;
    internal int? LanLevel => lanLevel;

    internal IcomLanModRegister NextPoll(int index) =>
      (index % 3) switch
      {
        0 => IcomLanModRegister.DataOffSource,
        1 => IcomLanModRegister.DataSource,
        _ => IcomLanModRegister.LanLevel
      };

    internal bool Apply(IcomLanModReading reading, bool autoSelectLan)
    {
      switch (reading.Register)
      {
        case IcomLanModRegister.DataOffSource:
          if (dataOff != reading.Value) dataOffWriteAttempts = 0;
          dataOff = reading.Value;
          break;
        case IcomLanModRegister.DataSource:
          if (data != reading.Value) dataWriteAttempts = 0;
          data = reading.Value;
          break;
        case IcomLanModRegister.LanLevel:
          lanLevel = reading.Value;
          break;
      }

      Volatile.Write(ref confirmedWrong,
        (dataOff.HasValue && dataOff.Value != 5 ||
         data.HasValue && data.Value != 5) ? 1 : 0);
      Volatile.Write(ref levelZero, lanLevel == 0 ? 1 : 0);
      Volatile.Write(ref description,
        $"DATA OFF={IcomLanModCivCodec.DescribeSource(dataOff)}, " +
        $"DATA={IcomLanModCivCodec.DescribeSource(data)}, " +
        $"LAN MOD Level={IcomLanModCivCodec.DescribeLevel(lanLevel)}; " +
        $"auto LAN source={(autoSelectLan ? "ON" : "OFF")}");
      return reading.Register is IcomLanModRegister.DataOffSource or
        IcomLanModRegister.DataSource;
    }

    internal bool TryGetCorrection(
      IcomLanModReading reading, bool autoSelectLan,
      out IcomLanModRegister register)
    {
      register = reading.Register;
      if (!autoSelectLan || reading.Value == IcomLanModCivCodec.LanSource)
        return false;

      // A CI-V write can be lost even on a healthy LAN. Retry only after a
      // fresh radio register reply confirms that the source is still not LAN.
      // Bound writes per observed value rather than flooding the CAT link.
      if (register == IcomLanModRegister.DataOffSource &&
          dataOffWriteAttempts < 3)
      {
        dataOffWriteAttempts++;
        return true;
      }
      if (register == IcomLanModRegister.DataSource &&
          dataWriteAttempts < 3)
      {
        dataWriteAttempts++;
        return true;
      }
      return false;
    }
  }
}
