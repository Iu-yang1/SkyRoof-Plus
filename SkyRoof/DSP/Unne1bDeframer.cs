using VE3NEA.SkyTlm.Core;
using VE3NEA.SkyTlm.Deframing;

namespace SkyRoof
{
  /// <summary>
  /// UNNE-1B (NORAD 101101) link framing. The vendored GENESIS deframer
  /// handles HADES-SA only (different length table, voice packet type/address).
  /// The existing FSK modem supplies soft symbols; this satellite-specific
  /// stage handles BF35 sync, type-C header, CRC on scrambled bytes, and the
  /// GENESIS x^17+x^12+1 descrambler. No changes to the vendored DLL.
  /// Reference: AMSAT-EA UNNE-1B Transmissions Overview V1.04 (2026-02-11).
  /// </summary>
  internal static class Unne1bDeframer
  {
    internal const int NoradId = 101101;
    private const int SyncBits = 16;
    private const ushort SyncWord = 0xBF35;

    // Bytes counted from the type/address byte, including the 2-byte
    // CCITT-FALSE FCS; derived from the packet tables in the official PDF.
    private static readonly IReadOnlyDictionary<int, int> PacketLengths =
      new Dictionary<int, int>
      {
        [1] = 31, [2] = 17, [3] = 29, [4] = 35,
        [5] = 27, [6] = 135, [8] = 31, [9] = 123,
        [10] = 17, [12] = 64, [14] = 38
      };

    internal static IEnumerable<Frame> Decode(SoftSymbols symbols)
    {
      float[] soft = symbols.Soft;
      int n = soft.Length;
      ushort window = 0;
      for (int i = 0; i < n; i++)
      {
        window = (ushort)((window << 1) | SoftBits.Hard(soft[i]));
        if (i < SyncBits - 1) continue;

        // Binary FSK polarity depends on the discriminator/mark convention.
        bool normal = window == SyncWord;
        bool inverted = window == unchecked((ushort)~SyncWord);
        if (!normal && !inverted) continue;

        int dataStart = i + 1;
        int invert = inverted ? 1 : 0;
        if (dataStart + 8 > n) continue;
        int first = ReadByte(soft, dataStart, invert);
        int headerStart = dataStart;
        int length;
        bool voice = first == 37;
        if (voice)
        {
          // Type 15 has an explicit SIZE byte before the type-C header.
          if (dataStart + 16 > n || ReadByte(soft, dataStart + 8, invert) != 0xFC) continue;
          headerStart += 8;
          length = 37;
        }
        else
        {
          if ((first & 0x0F) != 0x0C) continue;
          if (!PacketLengths.TryGetValue(first >> 4, out length)) continue;
        }

        if (headerStart + 8 * length > n) continue;
        byte[] wire = new byte[length];
        for (int b = 0; b < length; b++)
          wire[b] = (byte)ReadByte(soft, headerStart + 8 * b, invert);

        Frame? frame = TryParsePacket(wire, voice);
        if (frame == null) continue;
        yield return frame with
        {
          SoftBitOffset = i + 1 - SyncBits,
          SoftBitEnd = headerStart + 8 * length
        };
        i = headerStart + 8 * length - 1;
      }
    }

    internal static Frame? TryParsePacket(byte[] wire, bool voice = false)
    {
      if (wire.Length < 3 || (wire[0] & 0x0F) != 0x0C) return null;
      int type = wire[0] >> 4;
      if (voice)
      {
        if (type != 15 || wire.Length != 37) return null;
        // The type-15 37-byte CODEC2 subframe carries no CCITT-FALSE FCS.
        return new Frame
        {
          Bytes = (byte[])wire.Clone(), Framing = Framing.HADES,
          CrcValid = null, Note = "UNNE-1B CODEC2 subframe"
        };
      }

      if (!PacketLengths.TryGetValue(type, out int length) || length != wire.Length) return null;
      ushort fcs = (ushort)((wire[^2] << 8) | wire[^1]);
      if (Crc16CcittFalse.Compute(wire.AsSpan(0, wire.Length - 2)) != fcs)
        return null;
      byte[] payload = wire[..^2];
      Descramble(payload);
      return new Frame
      {
        Bytes = payload, CrcValid = true, Fcs = fcs,
        Framing = Framing.HADES, Note = "UNNE-1B type " + type
      };
    }

    private static int ReadByte(float[] soft, int start, int invert)
    {
      int b = 0;
      for (int i = 0; i < 8; i++)
        b = (b << 1) | (SoftBits.Hard(soft[start + i]) ^ invert);
      return b;
    }

    // AMSAT-EA GENESIS wire compatibility: the initial state is 0x2C350000,
    // reduced to 17 bits. The type/address byte is plaintext. In the reference
    // implementation, only bits 7..1 of subsequent bytes pass through the
    // LFSR; bit 0 is passed through unchanged (no LFSR advance).
    internal static void Descramble(byte[] packet)
    {
      int state = 1 << 16;
      for (int j = 1; j < packet.Length; j++)
      {
        int result = 0;
        for (int k = 0; k < 7; k++)
        {
          int received = (packet[j] >> (7 - k)) & 1;
          int decoded = received ^ ((state >> 16) ^ (state >> 11)) & 1;
          result = (result << 1) | decoded;
          state = ((state << 1) | received) & 0x1ffff;
        }
        packet[j] = (byte)((result << 1) | (packet[j] & 1));
      }
    }
  }
}
