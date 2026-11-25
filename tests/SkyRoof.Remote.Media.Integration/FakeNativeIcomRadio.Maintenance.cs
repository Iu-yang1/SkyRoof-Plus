using System.Buffers.Binary;
using System.Net;
using SkyRoof.Remote.Foundation.IcomLan;

internal sealed partial class FakeNativeIcomRadio
{
    private int _renewals;
    public int RenewalCount => Volatile.Read(ref _renewals);

    private async Task MaintainControlAsync(IPEndPoint peer, uint local, CancellationToken ct)
    {
        while (true)
        {
            var request = await _control.ReceiveAsync(ct);
            byte[] data = request.Buffer;
            if (!request.RemoteEndPoint.Equals(peer) ||
                !IcomLanControlCodec.MatchesEnvelope(data, ControlSid, local)) continue;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2));
            if (type == 5 && data.Length == 16) return;
            if (type == 7 && data.Length == 21 && data[16] == 0)
            {
                data[16] = 1;
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), ControlSid);
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), local);
                await _control.SendAsync(data, peer, ct);
                continue;
            }
            if (type != 0 || data.Length != 0x40 || data[0x15] is not (1 or 5)) continue;
            if (!data.AsSpan(0x1A, 6).SequenceEqual(_auth))
                throw new InvalidDataException("Control renewal/logout did not use the current token.");
            byte kind = data[0x15];
            if (kind == 5)
            {
                uint next = BinaryPrimitives.ReadUInt32LittleEndian(_auth.AsSpan(2)) + 1;
                BinaryPrimitives.WriteUInt32LittleEndian(_auth.AsSpan(2), next);
                Interlocked.Increment(ref _renewals);
            }
            await _control.SendAsync(Reply(0x40, ControlSid, local, Sequence(data), 2, kind, _auth), peer, ct);
            if (kind == 5) Console.WriteLine("PASS: correlated control token renewed during native session.");
        }
    }
}
