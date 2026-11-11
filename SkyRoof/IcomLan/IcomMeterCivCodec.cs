using System.Globalization;

namespace SkyRoof
{
  internal enum IcomMeterKind : byte
  {
    S = 0x02,
    Power = 0x11,
    Swr = 0x12,
    Alc = 0x13
  }

  internal readonly record struct IcomMeterReading(IcomMeterKind Kind, int Raw);

  internal readonly record struct IcomMeterSample(int Raw, DateTime TimestampUtc)
  {
    internal bool Fresh(DateTime now) =>
      (now - TimestampUtc).TotalSeconds is >= 0 and < 3;
  }

  internal sealed record IcomMeterSnapshot(
    IcomMeterSample? S = null,
    IcomMeterSample? Power = null,
    IcomMeterSample? Alc = null,
    IcomMeterSample? Swr = null)
  {
    internal IcomMeterSnapshot With(IcomMeterReading reading, DateTime now) =>
      reading.Kind switch
      {
        IcomMeterKind.S => this with { S = new(reading.Raw, now) },
        IcomMeterKind.Power => this with { Power = new(reading.Raw, now) },
        IcomMeterKind.Alc => this with { Alc = new(reading.Raw, now) },
        IcomMeterKind.Swr => this with { Swr = new(reading.Raw, now) },
        _ => this
      };
  }

  /// <summary>
  /// IC-9700 CI-V 15 xx readback. Levels are BCD 0000..0255, *not* Watts
  /// or a linear SWR measurement. Only replies from the negotiated radio
  /// to our controller are accepted.
  /// </summary>
  internal static class IcomMeterCivCodec
  {
    internal static byte[] BuildRead(byte radioAddress, IcomMeterKind kind) =>
      [0xFE, 0xFE, radioAddress, 0xE0, 0x15, (byte)kind, 0xFD];

    internal static bool TryParse(
      ReadOnlySpan<byte> frame, byte radioAddress, out IcomMeterReading reading)
    {
      reading = default;
      if (frame.Length != 9 ||
          frame[0] != 0xFE || frame[1] != 0xFE ||
          (frame[2] != 0xE0 && frame[2] != 0x00) ||
          frame[3] != radioAddress ||
          frame[4] != 0x15 || frame[8] != 0xFD ||
          !Enum.IsDefined(typeof(IcomMeterKind), frame[5]) ||
          !ValidBcd(frame[6]) || !ValidBcd(frame[7]))
        return false;

      int value = 1000 * (frame[6] >> 4) +
                  100 * (frame[6] & 0x0F) +
                  10 * (frame[7] >> 4) +
                  (frame[7] & 0x0F);
      // CI-V level is conventionally 0000–0255.
      if (value > 255)
        return false;
      reading = new((IcomMeterKind)frame[5], value);
      return true;
    }

    private static bool ValidBcd(byte b) =>
      (b >> 4) <= 9 && (b & 0x0F) <= 9;

    internal static string Display(IcomMeterKind kind, int raw) =>
      kind switch
      {
        IcomMeterKind.S => raw <= 120
          ? "S" + Math.Clamp((int)Math.Round(raw / (120.0 / 9)), 0, 9)
          : "S9+" + Math.Clamp((int)Math.Round((raw - 120) * 60.0 / 121), 0, 60),
        IcomMeterKind.Power => Math.Round(PowerPercent(raw))
          .ToString("0", CultureInfo.InvariantCulture) + "%",
        IcomMeterKind.Alc => Math.Round(raw * 100.0 / 120)
          .ToString("0", CultureInfo.InvariantCulture) + "%",
        IcomMeterKind.Swr => SwrRatio(raw)
          .ToString("0.0", CultureInfo.InvariantCulture),
        _ => "--"
      };

    internal static double PowerPercent(int raw) =>
      raw <= 143 ? Math.Clamp(raw * 50.0 / 143, 0, 50)
      : Math.Clamp(50 + (raw - 143) * 50.0 / 70, 50, 100);

    internal static double SwrRatio(int raw) =>
      raw <= 48 ? 1.0 + Math.Max(0, raw) * 0.5 / 48
      : raw <= 80 ? 1.5 + (raw - 48) * 0.5 / 32
      : raw <= 120 ? 2.0 + (raw - 80) / 40.0
      : 3.0 + (raw - 120) / 40.0;
  }
}
