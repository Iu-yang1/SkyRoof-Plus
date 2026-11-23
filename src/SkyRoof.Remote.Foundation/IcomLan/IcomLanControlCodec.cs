using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace SkyRoof.Remote.Foundation.IcomLan;

/// <summary>Portable Icom Direct LAN control-plane packet encoding; does not create a radio session.</summary>
public sealed record IcomLanLoginParameters
{
    public required uint LocalSid { get; init; }
    public required uint RemoteSid { get; init; }
    public required ushort Sequence { get; init; }
    public required ushort TokenRequest { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string ClientName { get; init; }
}

public static partial class IcomLanControlCodec
{
    public static byte[] ControlPacket(ushort type, ushort outerSequence,
        uint localSid, uint remoteSid)
    {
        byte[] packet = new byte[16];
        packet[0] = 16;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), outerSequence);
        WriteSid(packet.AsSpan(8, 4), localSid);
        WriteSid(packet.AsSpan(12, 4), remoteSid);
        return packet;
    }

    public static uint BuildSessionId(IPAddress address, int localPort)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Icom LAN requires an IPv4 local address.");
        if (localPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(localPort));
        byte[] bytes = address.GetAddressBytes();
        return ((uint)bytes[2] << 24) | ((uint)bytes[3] << 16) | (uint)localPort;
    }

    public static byte[] BuildLogin(IcomLanLoginParameters request)
    {
        byte[] packet = new byte[0x80];
        BuildRequestHeader(packet, request.LocalSid, request.RemoteSid, request.Sequence, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0x1A, 2), request.TokenRequest);
        EncodePasscode(request.Username).CopyTo(packet, 0x40);
        EncodePasscode(request.Password).CopyTo(packet, 0x50);
        string title = string.IsNullOrWhiteSpace(request.ClientName) ? "icom-pc" : request.ClientName.Trim();
        WriteAsciiZ(packet.AsSpan(0x60, 16), title);
        return packet;
    }

    public static byte[] BuildAuth(uint localSid, uint remoteSid, ushort sequence,
        byte kind, ReadOnlySpan<byte> authId)
    {
        if (authId.Length != 6)
            throw new ArgumentException("Icom auth ID must be six bytes.", nameof(authId));
        byte[] packet = new byte[0x40];
        BuildRequestHeader(packet, localSid, remoteSid, sequence, kind);
        authId.CopyTo(packet.AsSpan(0x1A, 6));
        return packet;
    }

    private static void BuildRequestHeader(byte[] packet, uint localSid,
        uint remoteSid, ushort sequence, byte kind)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
        WriteSid(packet.AsSpan(8, 4), localSid);
        WriteSid(packet.AsSpan(12, 4), remoteSid);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0x10, 4),
            (uint)(packet.Length - 0x10));
        packet[0x14] = 1;
        packet[0x15] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0x16, 2), sequence);
    }

    private static void WriteSid(Span<byte> destination, uint sid) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination, sid);

    private static void WriteAsciiZ(Span<byte> destination, string value)
    {
        destination.Clear();
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length - 1))
            .CopyTo(destination);
    }

    public static byte[] EncodePasscode(string value)
    {
        ReadOnlySpan<byte> substitutions =
        [
            0x47,0x5D,0x4C,0x42,0x66,0x20,0x23,0x46,0x4E,0x57,0x45,0x3D,
            0x67,0x76,0x60,0x41,0x62,0x39,0x59,0x2D,0x68,0x7E,0x7C,0x65,
            0x7D,0x49,0x29,0x72,0x73,0x78,0x21,0x6E,0x5A,0x5E,0x4A,0x3E,
            0x71,0x2C,0x2A,0x54,0x3C,0x3A,0x63,0x4F,0x43,0x75,0x27,0x79,
            0x5B,0x35,0x70,0x48,0x6B,0x56,0x6F,0x34,0x32,0x6C,0x30,0x61,
            0x6D,0x7B,0x2F,0x4B,0x64,0x38,0x2B,0x2E,0x50,0x40,0x3F,0x55,
            0x33,0x37,0x25,0x77,0x24,0x26,0x74,0x6A,0x28,0x53,0x4D,0x69,
            0x22,0x5C,0x44,0x31,0x36,0x58,0x3B,0x7A,0x51,0x5F,0x52
        ];
        byte[] input = Encoding.ASCII.GetBytes(value);
        byte[] result = new byte[16];
        for (int i = 0; i < result.Length && i < input.Length; i++)
        {
            int index = input[i] + i;
            if (index > 126) index = 32 + index % 127;
            if (index is >= 32 and <= 126)
                result[i] = substitutions[index - 32];
        }
        return result;
    }
}
