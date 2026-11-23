using System.Buffers.Binary;
using System.Net;
using SkyRoof.Remote.Foundation.IcomLan;

internal static class IcomLanControlTests
{
    public static void Run()
    {
        const uint local = 0x11223344, remote = 0xAABBCCDD;
        byte[] authId = [0x34, 0x12, 0x78, 0x56, 0x34, 0x12];
        Check(IcomLanControlCodec.BuildSessionId(IPAddress.Parse("192.168.1.25"), 41002)
            == 0x0119A02A, "SID must derive from local IPv4 and socket port");
        Check(IcomLanControlCodec.EncodePasscode("abc").Take(3)
            .SequenceEqual(new byte[] { 0x38, 0x2E, 0x40 }), "Icom credential substitution");
        var login = IcomLanControlCodec.BuildLogin(
            local, remote, 0x1234, 0x5678, "operator", "secret", "icom-pc");
        Check(login.Length == 0x80 && login[0x16] == 0x12 && login[0x17] == 0x34 &&
            login[0x1A] == 0x78 && login[0x1B] == 0x56 &&
            BinaryPrimitives.ReadUInt32BigEndian(login.AsSpan(8, 4)) == local,
            "Login request wire fields differ from original SkyRoof");
        var auth = IcomLanControlCodec.BuildAuth(local, remote, 0xABCD, 5, authId);
        Check(auth.Length == 0x40 && auth[0x16] == 0xAB && auth[0x17] == 0xCD &&
            auth.AsSpan(0x1A, 6).SequenceEqual(authId),
            "Token request differs from existing Direct LAN");
        var request = IcomLanControlCodec.BuildStreamRequest(new(
            local, remote, 0x0102, authId, "IC-9700", "operator", 0x8010,
            Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(),
            [1, 2, 3, 4, 5, 6], 0x018B, 41002, 41003));
        Check(request.Length == 0x90 && request[0x27] == 0x80 &&
            request[0x28] == 0x10 && request[0x72] == 4 &&
            BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(0x74, 4)) == 48000 &&
            BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(0x7C, 4)) == 41002 &&
            BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(0x80, 4)) == 41003,
            "Icom stream request capability / port / codec fields diverged");
        Check(IcomLanControlCodec.SelectReceiveSampleRate(0x40) == 44100 &&
            IcomLanControlCodec.SelectReceiveSampleRate(0) == 0,
            "Unadvertised sample rate selected");

        byte[] greeting = IcomLanControlCodec.ControlPacket(4, 0, remote, local);
        Check(IcomLanControlCodec.TryReadGreeting(greeting, local, out var remoteSid) &&
            remoteSid == remote, "Correlated session greeting rejected");
        Check(!IcomLanControlCodec.TryReadGreeting(greeting, remote, out _) &&
            IcomLanControlCodec.MatchesReady(
                IcomLanControlCodec.ControlPacket(6, 1, remote, local), local, remote),
            "Wrong SID accepted on greeting/ready");
        var loginReply = Reply(0x60, local, remote, 0x1234, 0, 0, authId);
        BinaryPrimitives.WriteUInt16LittleEndian(loginReply.AsSpan(0x1A, 2), 0x5678);
        Check(IcomLanControlCodec.MatchesLogin(loginReply, local, remote, 0x1234, 0x5678) &&
            !IcomLanControlCodec.MatchesLogin(loginReply, local, remote, 0x1234, 0x5677),
            "Login must match both sequence and token");
        var status = Reply(0x50, local, remote, 0x0102, 2, 3, authId);
        BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(0x42, 2), 50002);
        BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(0x46, 2), 50003);
        Check(IcomLanControlCodec.TryReadStreamReply(
            status, local, remote, 0x0102, authId, out var grant) &&
            grant.Accepted && grant.CivPort == 50002 && grant.AudioPort == 50003,
            "Stream media grant rejected");
        byte[] wrong = status.ToArray();
        wrong[0x1F] ^= 1;
        Check(!IcomLanControlCodec.TryReadStreamReply(
            wrong, local, remote, 0x0102, authId, out _), "Wrong auth ID accepted");
        Check(!IcomLanControlCodec.TryReadStreamReply(
            status, local, remote, 0x0103, authId, out _), "Wrong transaction accepted");
        Check(!IcomLanControlCodec.TryReadStreamReply(
            status, local + 1, remote, 0x0102, authId, out _), "Wrong recipient SID accepted");
        BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(0x30, 4), 0x80000001);
        Check(IcomLanControlCodec.TryReadStreamReply(
            status, local, remote, 0x0102, authId, out grant) && grant.Rejected,
            "Rejected radio grant silently accepted");
        var authReply = Reply(0x40, local, remote, 0x0200, 2, 5, authId);
        Check(IcomLanControlCodec.MatchesAuth(
            authReply, local, remote, 0x0200, 5, authId), "Token reply correlation");
        var capabilities = Reply(0x42 + 0x66, local, remote, 0x0300, 2, 2, authId);
        Check(IcomLanControlCodec.MatchesCapabilities(
            capabilities, local, remote, 0x0300, authId), "Capabilities correlation");
        Console.WriteLine("PASS: Icom Direct LAN control login/auth/capabilities/grant framing and wrong-transaction rejection.");
    }

    private static byte[] Reply(int size, uint local, uint remote, ushort seq,
        byte type, byte kind, byte[] auth)
    {
        var reply = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(reply, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8, 4), remote);
        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12, 4), local);
        reply[0x14] = type;
        reply[0x15] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(0x16, 2), seq);
        auth.CopyTo(reply, 0x1A);
        return reply;
    }

    private static void Check(bool valid, string note)
    {
        if (!valid) throw new InvalidOperationException("Icom control packet: " + note);
    }
}
