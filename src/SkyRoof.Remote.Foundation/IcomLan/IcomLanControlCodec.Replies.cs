using System.Buffers.Binary;

namespace SkyRoof.Remote.Foundation.IcomLan;

/// <summary>Correlated grant frame received only after the owning session requested a stream.</summary>
public readonly record struct IcomLanStreamReply(
    bool Accepted, bool Rejected, uint ErrorCode, int CivPort, int AudioPort);

public static partial class IcomLanControlCodec
{
    public static bool MatchesEnvelope(ReadOnlySpan<byte> frame,
        uint localSid, uint remoteSid) =>
        frame.Length >= 16 &&
        BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(8, 4)) == remoteSid &&
        BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(12, 4)) == localSid;

    public static bool TryReadGreeting(ReadOnlySpan<byte> frame,
        uint localSid, out uint remoteSid)
    {
        remoteSid = 0;
        if (frame.Length != 16 ||
            BinaryPrimitives.ReadUInt32LittleEndian(frame[..4]) != 16 ||
            BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2)) != 4 ||
            BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(12, 4)) != localSid)
            return false;
        remoteSid = BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(8, 4));
        return remoteSid != 0;
    }

    public static bool MatchesReady(ReadOnlySpan<byte> frame,
        uint localSid, uint remoteSid) =>
        frame.Length == 16 &&
        BinaryPrimitives.ReadUInt32LittleEndian(frame[..4]) == 16 &&
        BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2)) == 6 &&
        MatchesEnvelope(frame, localSid, remoteSid);

    public static bool MatchesLogin(ReadOnlySpan<byte> frame,
        uint localSid, uint remoteSid, ushort sequence, ushort tokenRequest) =>
        MatchesReply(frame, 0x60, localSid, remoteSid, sequence, 0, 0) &&
        BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(0x1A, 2)) == tokenRequest;

    public static bool MatchesAuth(ReadOnlySpan<byte> frame,
        uint localSid, uint remoteSid, ushort sequence, byte kind,
        ReadOnlySpan<byte> authId) =>
        authId.Length == 6 && MatchesReply(frame, 0x40, localSid, remoteSid,
            sequence, 2, kind) &&
        frame.Slice(0x1A, 2).SequenceEqual(authId[..2]);

    public static bool MatchesCapabilities(ReadOnlySpan<byte> frame,
        uint localSid, uint remoteSid, ushort sequence, ReadOnlySpan<byte> authId) =>
        authId.Length == 6 && frame.Length >= 0x42 + 0x66 &&
        (frame.Length - 0x42) % 0x66 == 0 &&
        MatchesReply(frame, frame.Length, localSid, remoteSid, sequence, 2, 2) &&
        frame.Slice(0x1A, 6).SequenceEqual(authId);

    public static bool TryReadStreamReply(ReadOnlySpan<byte> frame,
        uint localSid, uint remoteSid, ushort sequence,
        ReadOnlySpan<byte> authId, out IcomLanStreamReply reply)
    {
        reply = default;
        if (authId.Length != 6 || frame.Length is not (0x50 or 0x90) ||
            !MatchesReply(frame, frame.Length, localSid, remoteSid, sequence, 2, 3) ||
            !frame.Slice(0x1A, 6).SequenceEqual(authId))
            return false;
        if (frame.Length == 0x90)
        {
            reply = new IcomLanStreamReply(frame[0x60] == 1,
                frame[0x60] != 1, 0, 0, 0);
        }
        else
        {
            uint error = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(0x30, 4));
            bool disconnected = frame[0x40] == 1;
            int civ = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(0x42, 2));
            int audio = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(0x46, 2));
            reply = new IcomLanStreamReply(error == 0 && !disconnected && civ > 0,
                error != 0 || disconnected, error, civ, audio);
        }
        return true;
    }

    private static bool MatchesReply(ReadOnlySpan<byte> frame, int length,
        uint localSid, uint remoteSid, ushort sequence, byte type, byte kind) =>
        frame.Length == length &&
        BinaryPrimitives.ReadUInt32LittleEndian(frame[..4]) == length &&
        BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2)) == 0 &&
        MatchesEnvelope(frame, localSid, remoteSid) &&
        frame[0x14] == type && frame[0x15] == kind &&
        BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(0x16, 2)) == sequence;
}
