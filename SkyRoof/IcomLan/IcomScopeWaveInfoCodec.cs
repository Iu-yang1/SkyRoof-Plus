namespace SkyRoof
{
  /// <summary>
  /// Decodes CI-V 27 00 waveform metadata without assuming IC-9700 field widths.
  /// IC-905 10 GHz CENTER uses 6-byte center + 5-byte span, while
  /// FIXED/SCROLL uses two 6-byte edges. The signed lower-edge marker
  /// is permitted only in the 100 GHz BCD digit of the first edge.
  /// </summary>
  internal static class IcomScopeWaveInfoCodec
  {
    private const int MetadataStart = 9;
    private const int FieldsStart = 10;
    private const long TenGigahertzHz = 10_000_000_000L;

    internal static bool TryDecode(
      byte[] frame,
      IcomRadioProfile radio,
      bool singleFrame,
      out byte mode,
      out long frequencyA,
      out long frequencyB,
      out bool outOfRange,
      out int samplesStart)
    {
      mode = 0;
      frequencyA = 0;
      frequencyB = 0;
      outOfRange = false;
      samplesStart = 0;

      if (frame.Length < 22 || frame[^1] != 0xFD)
        return false;

      byte rawMode = frame[MetadataStart];
      if (rawMode > 3)
        return false;

      // The five-byte CI-V form remains valid for all three radios.
      // Only IC-905 can use extended frequency fields in this profile.
      (int first, int second)[] layouts =
        radio.Model == "IC-905"
          ? rawMode == (byte)IcomScopeMode.Center
            ? [(5, 5), (6, 5)]
            : [(5, 5), (6, 6)]
          : [(5, 5)];

      foreach ((int first, int second) in layouts)
      {
        int outOfRangeIndex = FieldsStart + first + second;
        int dataStart = outOfRangeIndex + 1;
        if (frame.Length < dataStart + 1)
          continue;

        byte rangeFlag = frame[outOfRangeIndex];
        if (rangeFlag > 1)
          continue;

        int expectedPoints =
          singleFrame && rangeFlag == 0
            ? IcomScopeAssembler.ScopePointCount
            : 0;

        // A full LAN sweep has exactly 475 bins; USB division 01 has none.
        // The out-of-range reply omits bins in both transport formats.
        if (frame.Length != dataStart + expectedPoints + 1)
          continue;

        if (!TryReadFrequency(
              frame.AsSpan(FieldsStart, first),
              allowNegative: first == 6 &&
                rawMode != (byte)IcomScopeMode.Center,
              out long firstHz) ||
            !TryReadFrequency(
              frame.AsSpan(FieldsStart + first, second),
              allowNegative: false,
              out long secondHz))
          continue;

        if (first == 6 &&
            rawMode == (byte)IcomScopeMode.Center &&
            firstHz < TenGigahertzHz)
          continue;

        if (first == 6 &&
            rawMode != (byte)IcomScopeMode.Center &&
            firstHz < TenGigahertzHz &&
            secondHz < TenGigahertzHz)
          continue;

        mode = rawMode;
        frequencyA = firstHz;
        frequencyB = secondHz;
        outOfRange = rangeFlag != 0;
        samplesStart = dataStart;
        return true;
      }

      return false;
    }

    private static bool TryReadFrequency(
      ReadOnlySpan<byte> bytes,
      bool allowNegative,
      out long frequencyHz)
    {
      if (allowNegative && bytes.Length == 6 &&
          (bytes[^1] & 0xF0) == 0xF0)
      {
        byte[] magnitudeBytes = bytes.ToArray();
        magnitudeBytes[^1] &= 0x0F;
        if (!IcomCivFrequencyCodec.TryDecode(
              magnitudeBytes, out long magnitude) ||
            magnitude == 0)
        {
          frequencyHz = 0;
          return false;
        }

        frequencyHz = -magnitude;
        return true;
      }

      return IcomCivFrequencyCodec.TryDecode(bytes, out frequencyHz);
    }
  }
}
