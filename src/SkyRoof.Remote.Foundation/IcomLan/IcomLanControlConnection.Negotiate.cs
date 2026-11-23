using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SkyRoof.Remote.Foundation.IcomLan;

public sealed partial class IcomLanControlConnection
{
    private async Task NegotiateAsync(IcomLanConnectionOptions options, CancellationToken ct)
    {
        var hello = IcomLanControlCodec.ControlPacket(3, 0, LocalSid, 0);
        await SendAsync(hello, tracked: false, ct);
        await SendAsync(hello, tracked: false, ct);
        var greeting = await WaitForAsync(packet =>
            IcomLanControlCodec.TryReadGreeting(packet, LocalSid, out _),
            TimeSpan.FromSeconds(2), ct);
        if (!IcomLanControlCodec.TryReadGreeting(greeting, LocalSid, out uint remote))
            throw new InvalidDataException("Icom control greeting was malformed.");
        RemoteSid = remote;
        var ready = IcomLanControlCodec.ControlPacket(6, 1, LocalSid, RemoteSid);
        await SendAsync(ready, tracked: false, ct);
        await SendAsync(ready, tracked: false, ct);
        await WaitForAsync(packet =>
            IcomLanControlCodec.MatchesReady(packet, LocalSid, RemoteSid),
            TimeSpan.FromSeconds(2), ct);

        ushort tokenRequest = unchecked((ushort)RandomNumberGenerator.GetInt32(1, 65536));
        ushort loginSeq = NextInnerSequence();
        var login = IcomLanControlCodec.BuildLogin(LocalSid, RemoteSid,
            loginSeq, tokenRequest, options.Username, options.Password, options.ClientName);
        await SendAsync(login, tracked: true, ct);
        var loginReply = await WaitForAsync(packet =>
            IcomLanControlCodec.MatchesLogin(packet, LocalSid, RemoteSid,
                loginSeq, tokenRequest), TimeSpan.FromSeconds(3), ct);
        if (loginReply.AsSpan(0x30, 4).SequenceEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFE }))
            throw new UnauthorizedAccessException("Icom LAN rejected the supplied credentials.");
        _authId = loginReply.AsSpan(0x1A, 6).ToArray();
        _authenticated = true;

        ushort capabilitySeq = NextInnerSequence();
        ushort tokenSeq = NextInnerSequence();
        var requestedAuthId = _authId.ToArray();
        await SendAsync(IcomLanControlCodec.BuildAuth(
            LocalSid, RemoteSid, capabilitySeq, 2, requestedAuthId), true, ct);
        await SendAsync(IcomLanControlCodec.BuildAuth(
            LocalSid, RemoteSid, tokenSeq, 5, requestedAuthId), true, ct);

        var capability = await WaitForCapabilitiesAsync(capabilitySeq,
            tokenSeq, requestedAuthId, ct);
        Capabilities = capability;
        uint negotiated = IcomLanControlCodec.SelectReceiveSampleRate(capability.RxSampleMask);
        if (options.PreferredRxRate != 0)
        {
            ushort selectedBit = options.PreferredRxRate switch
            {
                48000 => 0x0008, 32000 => 0x0004, 16000 => 0x0002,
                12000 => 0x0080, 44100 => 0x0040, 22050 => 0x0020,
                11025 => 0x0010, 8000 => 0x0001, _ => (ushort)0
            };
            if ((capability.RxSampleMask & selectedBit) != 0)
                negotiated = options.PreferredRxRate;
        }
        NegotiatedRxRate = negotiated;
        ushort streamSeq = NextInnerSequence();
        await SendAsync(IcomLanControlCodec.BuildStreamRequest(new(
            LocalSid, RemoteSid, streamSeq, _authId, capability.RadioName,
            options.Username, capability.CommonCapability, capability.Guid,
            capability.Mac, capability.RxSampleMask,
            options.LocalCivPort, options.LocalAudioPort,
            RxCodec: options.RxCodec, RxSampleRate: negotiated)), true, ct);
        await AwaitStreamGrantAsync(streamSeq, ct);
    }

    private async Task<IcomLanRadioCapabilities> WaitForCapabilitiesAsync(
        ushort capabilitySeq, ushort tokenSeq, byte[] expectedAuthId, CancellationToken ct)
    {
        IcomLanRadioCapabilities? capabilities = null;
        bool hasToken = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (!hasToken || capabilities is null)
            {
                var packet = (await _control.ReceiveAsync(deadline.Token)).Buffer;
                if (IcomLanControlCodec.MatchesCapabilities(packet, LocalSid, RemoteSid,
                    capabilitySeq, expectedAuthId))
                {
                    if (!IcomLanControlCodec.TryParseCapabilities(packet, out var found))
                        throw new InvalidDataException("Icom capabilities lacked a usable radio.");
                    capabilities = found;
                }
                else if (IcomLanControlCodec.MatchesAuth(packet, LocalSid, RemoteSid,
                    tokenSeq, 5, expectedAuthId))
                {
                    _authId = packet.AsSpan(0x1A, 6).ToArray();
                    hasToken = true;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Icom capabilities or auth token reply timed out.");
        }
        return capabilities;
    }

    private async Task AwaitStreamGrantAsync(ushort seq, CancellationToken ct)
    {
        var reply = await WaitForAsync(packet =>
            IcomLanControlCodec.TryReadStreamReply(
                packet, LocalSid, RemoteSid, seq, _authId, out _),
            TimeSpan.FromSeconds(6), ct);
        if (!IcomLanControlCodec.TryReadStreamReply(
            reply, LocalSid, RemoteSid, seq, _authId, out var grant) ||
            grant.Rejected)
            throw new IOException("Icom radio refused authenticated media stream grant.");
        if (!grant.Accepted)
            throw new IOException("Icom radio grant omitted a usable CI-V endpoint.");
        if (grant.CivPort > 0) RemoteCivPort = grant.CivPort;
        if (grant.AudioPort > 0) RemoteAudioPort = grant.AudioPort;
    }
}
