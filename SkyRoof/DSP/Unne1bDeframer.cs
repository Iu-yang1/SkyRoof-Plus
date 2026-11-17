using System.Numerics;
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

    // A weak 200-baud FSK burst can have isolated unreliable soft decisions
    // even when the transmitter's original packet is recoverable. Repair only
    // CRC-protected type-C telemetry, and accept a correction solely when the
    // complete scrambled wire packet passes CRC-CCITT-FALSE.
    private const int MaxSoftBitRepairs = 3;
    private const int WeakestBitCandidates = 16;

    internal static IEnumerable<Frame> Decode(SoftSymbols symbols)
    {
      float[] soft = symbols.Soft;
      int n = soft.Length;
      ushort window = 0;
      for (int i = 0; i < n; i++)
      {
        window = (ushort)((window << 1) | SoftBits.Hard(soft[i]));
        if (i < SyncBits - 1) continue;

        // Mark/space polarity is determined by the received sync. Allow one
        // uncertain sync bit only for CRC-protected frames; CODEC2 has no FCS.
        int normalErrors = BitOperations.PopCount((uint)(window ^ SyncWord));
        int invertedErrors = BitOperations.PopCount(
          (uint)(window ^ unchecked((ushort)~SyncWord)));
        int syncErrors = Math.Min(normalErrors, invertedErrors);
        if (syncErrors > 1) continue;

        int dataStart = i + 1;
        int invert = invertedErrors < normalErrors ? 1 : 0;
        if (dataStart + 8 > n) continue;
        int first = ReadByte(soft, dataStart, invert);
        if (syncErrors == 0 && first == 37)
        {
          // Type 15 is the only UNNE-1B frame with a SIZE prefix and no
          // CRC. Do not use weak-bit repair or approximate sync for voice.
          int headerStart = dataStart + 8;
          const int voiceLength = 37;
          if (headerStart + voiceLength * 8 > n ||
              ReadByte(soft, headerStart, invert) != 0xFC) continue;
          var wire = ReadPacket(soft, headerStart, voiceLength, invert);
          Frame? frame = TryParsePacket(wire, voice: true);
          if (frame == null) continue;
          yield return frame with
          {
            SoftBitOffset = i + 1 - SyncBits,
            SoftBitEnd = headerStart + voiceLength * 8
          };
          i = headerStart + voiceLength * 8 - 1;
          continue;
        }

        // Only the 11 protocol-defined telemetry types with source address C
        // are legal. A single uncertain header bit can be reconstructed from
        // those hypotheses, but CRC must validate the complete corrected
        // packet before any frame is emitted.
        foreach (var (type, length) in PacketLengths)
        {
          int expectedHeader = (type << 4) | 0x0C;
          int headerErrors = BitOperations.PopCount((uint)(first ^ expectedHeader));
          if (headerErrors > 1 || dataStart + 8 * length > n) continue;

          byte[] wire = ReadPacket(soft, dataStart, length, invert);
          wire[0] = (byte)expectedHeader;
          Frame? frame = TryParsePacket(wire);
          int payloadRepairs = 0;
          if (frame == null)
          {
            // A nearly matched sync expands the number of candidates in
            // noise. Bound the search to two other bit flips in that case.
            int budget = Math.Min(MaxSoftBitRepairs - headerErrors,
              syncErrors == 0 ? MaxSoftBitRepairs : 2);
            if (budget > 0)
              frame = TryRepairPacket(wire, soft, dataStart, budget,
                out payloadRepairs);
          }
          if (frame == null) continue;

          int repaired = headerErrors + payloadRepairs;
          if (repaired != 0)
            frame = frame with
            {
              Note = frame.Note + $"; CRC-verified soft repair ({repaired} bit(s))"
            };
          yield return frame with
          {
            SoftBitOffset = i + 1 - SyncBits,
            SoftBitEnd = dataStart + 8 * length
          };
          i = dataStart + 8 * length - 1;
          break;
        }
      }
    }

    private static byte[] ReadPacket(float[] soft, int start, int length,
      int invert)
    {
      var wire = new byte[length];
      for (int b = 0; b < length; b++)
        wire[b] = (byte)ReadByte(soft, start + b * 8, invert);
      return wire;
    }

    // Retry flips only among the weakest 16 non-header soft decisions,
    // including received FCS bits. CRC covers the scrambled wire bytes;
    // descrambling takes place only after the candidate has passed CRC.
    private static Frame? TryRepairPacket(byte[] wire, float[] soft, int start,
      int maxFlips, out int repaired)
    {
      int[] weakest = Enumerable.Range(8, (wire.Length - 1) * 8)
        .OrderBy(bit => Math.Abs(soft[start + bit]))
        .Take(WeakestBitCandidates)
        .ToArray();
      for (int flips = 1; flips <= maxFlips; flips++)
      {
        if (TryFlipCandidates(wire, weakest, 0, flips, out var frame))
        {
          repaired = flips;
          return frame;
        }
      }
      repaired = 0;
      return null;
    }

    private static bool TryFlipCandidates(byte[] wire, int[] candidates,
      int next, int remaining, out Frame? frame)
    {
      if (remaining == 0)
      {
        frame = TryParsePacket(wire);
        return frame != null;
      }

      for (int i = next; i <= candidates.Length - remaining; i++)
      {
        int bit = candidates[i];
        int byteIndex = bit / 8;
        byte mask = (byte)(0x80 >> (bit % 8));
        wire[byteIndex] ^= mask;
        bool success = TryFlipCandidates(wire, candidates, i + 1,
          remaining - 1, out frame);
        wire[byteIndex] ^= mask;
        if (success) return true;
      }
      frame = null;
      return false;
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
