using VE3NEA.SkyTlm.Core;

namespace SkyRoof
{
  /// <summary>
  /// AO-123/ASRTU-1 CCSDS TM short-KISS transport, matched to
  /// gr-satellites: python/components/transports/tm_kiss_transport.py
  /// with short_tm=True and virtual_channels=[0,2].
  /// This is packet REASSEMBLY after CCSDS RS/FEC, not another RF demodulator.
  /// </summary>
  internal sealed class AsrtuTmShortKissTransport
  {
    private sealed class Channel
    {
      public readonly List<byte> Packet = new();
      public bool Escaped;
    }

    private readonly Dictionary<int, Channel> channels = new()
    {
      [0] = new Channel(), [2] = new Channel()
    };

    internal IEnumerable<Frame> Push(Frame transfer)
    {
      if (transfer.CrcValid != true || transfer.Bytes.Length <= 5)
      {
        // Any lost or unverified TM frame may contain the missing middle
        // fragment of a KISS packet; retaining partial bytes across it would
        // concatenate unrelated Space Packets. Reset both VC streams.
        foreach (Channel channel in channels.Values)
        {
          channel.Packet.Clear();
          channel.Escaped = false;
        }
        yield break;
      }

      byte[] frame = transfer.Bytes;
      // Short TM primary header (BY02): version(2), SCID(10), VCID(3),
      // OCF(1), master count(8), VC count(8), first pointer(8).
      int vc = (frame[1] >> 1) & 7;
      if (!channels.TryGetValue(vc, out Channel? state)) yield break;

      for (int i = 5; i < frame.Length; i++)
      {
        byte b = frame[i];
        if (b == 0xC0)
        {
          if (state.Packet.Count > 0)
          {
            var packet = state.Packet.ToArray();
            state.Packet.Clear();
            yield return new Frame
            {
              Bytes = packet, Framing = Framing.CCSDS,
              // The transfer passed CCSDS RS verification; KISS has no FCS.
              CrcValid = null,
              Note = $"AO-123 packet VC{vc} (TM RS verified)",
              BurstIndex = transfer.BurstIndex,
              TimeSeconds = transfer.TimeSeconds,
              CfoHz = transfer.CfoHz,
              SnrDb = transfer.SnrDb
            };
          }
          state.Escaped = false;
        }
        else if (state.Escaped)
        {
          if (b == 0xDC) state.Packet.Add(0xC0);
          else if (b == 0xDD) state.Packet.Add(0xDB);
          // Invalid KISS escape cannot be accepted as arbitrary payload.
          state.Escaped = false;
        }
        else if (b == 0xDB)
        {
          state.Escaped = true;
        }
        else
        {
          state.Packet.Add(b);
          // Bound an unfinished stream in case the sender goes away
          // before FEND; no valid 223-byte TM fragment requires 65536 B.
          if (state.Packet.Count > 65536)
          {
            state.Packet.Clear();
            state.Escaped = false;
          }
        }
      }
    }

    internal static IEnumerable<(string Name, string Value)> DescribeSpacePacket(byte[] packet)
    {
      if (packet.Length < 6) yield break;
      int version = packet[0] >> 5;
      int apid = ((packet[0] & 0x07) << 8) | packet[1];
      int sequence = ((packet[2] & 0x3F) << 8) | packet[3];
      int length = (packet[4] << 8) | packet[5];
      yield return ("Protocol", "CCSDS Space Packet (ASRTU)");
      yield return ("Version", version.ToString());
      yield return ("APID", apid.ToString());
      yield return ("Sequence", sequence.ToString());
      yield return ("Packet bytes declared", (length + 7).ToString());
      yield return ("Packet type", ((packet[0] >> 4) & 1) == 0 ? "Telemetry" : "Telecommand");
      // gr-satellites/telemetry/asrtu.py deliberately exposes payload raw;
      // don't invent engineering calibration without a source specification.
    }
  }
}
