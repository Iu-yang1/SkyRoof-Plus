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
        var login = IcomLanControlCodec.BuildLogin(new IcomLanLoginParameters
        {
            LocalSid = LocalSid, RemoteSid = RemoteSid,
            Sequence = loginSeq, TokenRequest = tokenRequest,
            Username = options.Username, Password = options.Password,
            ClientName = options.ClientName
        });
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
        uint negotiated = IcomLanControlCodec.SelectSampleRate(capability.RxSampleMask, options.PreferredRxRate);
        NegotiatedRxRate = negotiated;
        if (options.PreferredTxRate > 0)
        {
            NegotiatedTxRate = IcomLanControlCodec.SelectSampleRate(capability.TxSampleMask,
                options.PreferredTxRate);
            if (NegotiatedTxRate == 0 || !Enum.IsDefined(
                (SkyRoof.Remote.Foundation.Media.IcomLanAudioCodec)options.TxCodec))
                throw new IOException("Radio does not advertise a compatible TX audio format.");
            NegotiatedTxCodec = options.TxCodec;
        }
        ushort streamSeq = NextInnerSequence();
        await SendAsync(IcomLanControlCodec.BuildStreamRequest(new(
            LocalSid, RemoteSid, streamSeq, _authId, capability.RadioName,
            options.Username, capability.CommonCapability, capability.Guid,
            capability.Mac, capability.RxSampleMask,
            options.LocalCivPort, options.LocalAudioPort,
            RxCodec: options.RxCodec, RxSampleRate: negotiated,
            TxCodec: NegotiatedTxCodec, TxSampleRate: NegotiatedTxRate,
            TxBufferMs: NegotiatedTxRate == 0 ? 0 : options.TxBufferMs)), true, ct);
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
