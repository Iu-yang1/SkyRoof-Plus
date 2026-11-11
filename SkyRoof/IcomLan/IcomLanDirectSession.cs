using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Serilog;

namespace SkyRoof
{
  /// <summary>
  /// Minimal authenticated Icom LAN client used only for the native CI-V stream.
  ///
  /// It opens its own control session, authenticates with the radio, requests a
  /// CI-V media stream and enables model-selected Icom scope output on that stream.
  /// Unlike RS-BA1's virtual COM path, the LAN CI-V stream exposes the radio's
  /// combined one-division waveform, so a complete 475-bin sweep can be delivered
  /// in one CI-V frame.
  ///
  /// Packet layout and authentication follow the RS-BA1-compatible transport
  /// fields observed by wfview, SDR9700 and AetherSDR. Direct LAN is an
  /// experimental, independently authenticated session; it is never required
  /// for the normal SkyCAT or passive RS-BA1 paths.
  /// </summary>
  internal sealed class IcomLanDirectSession : IDisposable
  {
    private const int DefaultControlPort = 50001;
    private const int DefaultCivPort = 50002;
    private const int DefaultAudioPort = 50003;

    private const int LoginSize = 0x80;
    private const int LoginResponseSize = 0x60;
    private const int AuthSize = 0x40;
    private const int ConnInfoSize = 0x90;
    private const int StatusSize = 0x50;
    private const int CapabilitiesHeaderSize = 0x42;
    private const int RadioCapabilitySize = 0x66;
    private const ushort MacIdentityCapability = 0x8010;
    private const byte Lpcm16MonoCodec = 0x04;

    private readonly IPAddress RadioAddress;
    private readonly int ControlPort;
    private readonly string Username;
    private readonly string Password;
    private readonly string ClientName;
    private readonly IcomDirectLanSettings AudioOptions;

    private readonly Channel<float[]> TxAudio = Channel.CreateBounded<float[]>(
      new BoundedChannelOptions(12)
      {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.DropOldest
      });
    private readonly SemaphoreSlim AudioSendLock = new(1, 1);
    private readonly object AudioHistoryLock = new();
    private readonly Dictionary<ushort, byte[]> AudioTxHistory = new();
    private readonly Queue<ushort> AudioTxOrder = new();
    private ushort AudioOuterSeq = 1;
    private ushort AudioPayloadSeq;
    private uint NegotiatedRxRate;
    private uint NegotiatedTxRate;
    private volatile bool AudioTxReady;
    internal Func<bool>? IsPttActive { get; init; }

    private IcomMeterKind? PendingMeter;
    private DateTime PendingMeterUtc;
    private DateTime LastMeterPollUtc;
    private int NextMeterIndex;
    internal event Action<IcomMeterReading>? MeterReceived;

    private UdpClient? Control;
    private UdpClient? Civ;
    private UdpClient? AudioSink;

    private readonly SemaphoreSlim ControlSendLock = new(1, 1);
    private readonly SemaphoreSlim CivSendLock = new(1, 1);

    private readonly Dictionary<ushort, byte[]> ControlTxHistory = new();
    private readonly Queue<ushort> ControlTxOrder = new();
    private readonly Dictionary<ushort, byte[]> CivTxHistory = new();
    private readonly Queue<ushort> CivTxOrder = new();

    private ushort ControlOuterSeq = 1;
    private ushort CivOuterSeq = 1;
    private ushort CivInnerSeq;
    private ushort AuthInnerSeq;

    private uint ControlLocalSid;
    private uint ControlRemoteSid;
    private uint CivLocalSid;
    private uint CivRemoteSid;
    private uint AudioLocalSid;
    private uint AudioRemoteSid;

    private byte[] AuthId = new byte[6];
    private byte[] RadioGuid = new byte[16];
    private readonly byte[] RadioMacAddress = new byte[6];
    private ushort RadioCommonCapability;
    private ushort RadioRxSampleCapabilities;
    private ushort RadioTxSampleCapabilities;
    private uint RadioBaudRate;
    private string RadioAudioName = string.Empty;
    private byte RadioCivAddress;
    private string RadioName = string.Empty;
    private readonly IcomRadioProfile ExpectedScopeProfile;
    internal bool PreferFastScopeSweep { get; init; }
    private readonly IcomNativeCivSequenceTracker CivRxSequences = new();
    private long LastNativeScopeTicks;
    private int RemoteCivPort = DefaultCivPort;
    private int RemoteAudioPort = DefaultAudioPort;

    private ushort? PendingRenewalInnerSequence;
    private byte[] PendingRenewalAuthId = Array.Empty<byte>();
    private DateTime LastAuthRequestUtc = DateTime.MinValue;

    private DateTime LastControlIdleUtc = DateTime.MinValue;
    private DateTime LastControlPingUtc = DateTime.MinValue;
    private DateTime LastCivIdleUtc = DateTime.MinValue;
    private DateTime LastCivPingUtc = DateTime.MinValue;
    private DateTime LastAuthUtc = DateTime.MinValue;
    private DateTime LastScopeCommandUtc = DateTime.MinValue;

    private ushort ControlPingSeq = 2;
    private ushort CivPingSeq = 1;
    private ushort AudioPingSeq = 1;
    private ushort AudioPingInnerSeq = 0x8304;
    private ushort PingInnerSeq = 0x8304;

    private bool Disposed;

    internal event Action<byte[]>? CivDataReceived;
    internal event Action<string>? StatusChanged;
    internal event Action<int, IcomLanLpcmAudioCodec.RxFrame>? AudioFrameReceived;

    internal bool IsAuthenticated { get; private set; }
    internal bool IsStreaming { get; private set; }
    internal int LocalCivPort { get; private set; }
    internal int RemoteCivPortNumber => RemoteCivPort;
    internal long DatagramCount { get; private set; }
    internal long CivPayloadCount { get; private set; }
    internal long NativeCivDuplicates => CivRxSequences.Duplicates;
    internal long NativeCivSequenceGaps => CivRxSequences.MissingPackets;
    internal long NativeCivReordered => CivRxSequences.Reordered;

    internal void MarkNativeScopeWaveformReceived() =>
      Interlocked.Exchange(ref LastNativeScopeTicks, DateTime.UtcNow.Ticks);

    internal IcomLanDirectSession(
      string radioAddress,
      int controlPort,
      string username,
      string password,
      string clientName,
      IcomRadioProfile? expectedScopeProfile = null,
      IcomDirectLanSettings? audioSettings = null)
    {
      if (!IPAddress.TryParse(radioAddress, out IPAddress? address) ||
          address.AddressFamily != AddressFamily.InterNetwork)
        throw new ArgumentException(
          "Direct Icom LAN requires an explicit IPv4 radio address.",
          nameof(radioAddress));

      if (string.IsNullOrWhiteSpace(username))
        throw new ArgumentException(
          "Direct Icom LAN requires the radio network username.",
          nameof(username));

      if (string.IsNullOrEmpty(password))
        throw new ArgumentException(
          "Direct Icom LAN requires the radio network password.",
          nameof(password));

      RadioAddress = address;
      ExpectedScopeProfile = expectedScopeProfile ?? IcomRadioProfiles.Ic9700;
      ControlPort = Math.Clamp(controlPort, 1, 65535);
      Username = username.Trim();
      Password = password;
      ClientName = NormalizeClientName(clientName);
      AudioOptions = audioSettings ?? new IcomDirectLanSettings();
    }

    internal bool TryQueueTransmitAudio(float[] samples, int count)
    {
      // This transport never asserts PTT. An active CAT PTT readback is
      // required at both capture and UDP-send time; stale audio is discarded.
      if (!AudioOptions.Transmit.Enabled || !AudioTxReady ||
          IsPttActive?.Invoke() != true || count <= 0 ||
          count > samples.Length)
        return false;

      var copy = new float[count];
      Array.Copy(samples, copy, count);
      return TxAudio.Writer.TryWrite(copy);
    }

    internal async Task RunAsync(CancellationToken token)
    {
      ThrowIfDisposed();

      IPAddress localAddress = ResolveLocalAddress(RadioAddress, ControlPort);

      using UdpClient control = CreateBoundClient(localAddress);
      Control = control;
      control.Connect(RadioAddress, ControlPort);

      int controlLocalPort =
        ((IPEndPoint)control.Client.LocalEndPoint!).Port;
      ControlLocalSid = BuildSessionId(localAddress, controlLocalPort);

      using UdpClient civ = CreateBoundClient(localAddress);
      civ.Client.ReceiveBufferSize = 1_048_576;
      Civ = civ;
      LocalCivPort = ((IPEndPoint)civ.Client.LocalEndPoint!).Port;
      CivLocalSid = BuildSessionId(localAddress, LocalCivPort);

      using UdpClient audio = CreateBoundClient(localAddress);
      AudioSink = audio;
      int localAudioPort =
        ((IPEndPoint)audio.Client.LocalEndPoint!).Port;
      AudioLocalSid = BuildSessionId(localAddress, localAudioPort);

      Publish(
        $"Direct Icom LAN (experimental): authenticating {RadioAddress}:{ControlPort} " +
        $"as '{Username}'...");

      await StartCommonHandshakeAsync(
        control,
        isControl: true,
        token);

      (ushort loginSequence, ushort loginRequestId) =
        await SendLoginAsync(control, token);

      byte[] loginReply = await ReceiveMatchingAsync(
        control,
        packet => IsMatchingLoginReply(
          packet,
          ControlLocalSid,
          ControlRemoteSid,
          loginSequence,
          loginRequestId),
        TimeSpan.FromSeconds(3),
        token);

      if (loginReply.AsSpan(0x30, 4).SequenceEqual(
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFE }))
        throw new InvalidOperationException(
          "Icom radio rejected the network username/password.");

      AuthId = loginReply.AsSpan(0x1A, 6).ToArray();
      IsAuthenticated = true;
      Publish("Direct Icom LAN: login correlated; requesting radio capabilities and authentication token...");

      byte[] capabilitiesAuthId = AuthId.ToArray();
      ushort capabilitiesSequence =
        await SendAuthAsync(control, 0x02, token);

      byte[] initialAuthId = AuthId.ToArray();
      ushort initialAuthSequence =
        await SendAuthAsync(control, 0x05, token);
      LastAuthRequestUtc = DateTime.UtcNow;

      bool gotCapabilities = false;
      bool gotAuthOk = false;
      DateTime authDeadline = DateTime.UtcNow.AddSeconds(5);

      while ((!gotCapabilities || !gotAuthOk) &&
             DateTime.UtcNow < authDeadline)
      {
        byte[] packet = await ReceiveOneAsync(
          control,
          TimeSpan.FromMilliseconds(750),
          token);

        if (packet.Length == 0)
          continue;

        await HandleCommonPacketAsync(
          control,
          isControl: true,
          packet,
          token);

        if (!gotCapabilities &&
            IsMatchingCapabilitiesResponse(
              packet,
              ControlLocalSid,
              ControlRemoteSid,
              capabilitiesSequence,
              capabilitiesAuthId))
        {
          ParseCapabilities(packet);
          gotCapabilities = true;
          Publish(
            $"Direct Icom LAN: found {RadioName}, CI-V 0x{RadioCivAddress:X2}, " +
            $"RX caps 0x{RadioRxSampleCapabilities:X4}, TX caps 0x{RadioTxSampleCapabilities:X4}, " +
            $"baud {RadioBaudRate:N0}.");
        }
        else if (!gotAuthOk &&
                 IsMatchingAuthReply(
                   packet,
                   ControlLocalSid,
                   ControlRemoteSid,
                   0x05,
                   initialAuthSequence,
                   initialAuthId))
        {
          uint response = BinaryPrimitives.ReadUInt32LittleEndian(
            packet.AsSpan(0x30, 4));

          AuthId = packet.AsSpan(0x1A, 6).ToArray();
          gotAuthOk = true;
          LastAuthUtc = DateTime.UtcNow;
          PendingRenewalInnerSequence = null;

          if (response != 0)
          {
            Publish(
              $"Direct Icom LAN: radio reissued authentication state " +
              $"(0x{response:X8}); stream ownership still requires a correlated grant.");
          }
        }
      }

      if (!gotCapabilities)
        throw new TimeoutException(
          "Icom radio did not return a correlated LAN capabilities response.");

      if (!gotAuthOk)
        throw new TimeoutException(
          "Icom radio did not return a correlated authentication-token response.");

      // The authenticated radio capabilities reply identifies the hardware.
      // Match the configured decoder before opening any waveform control
      // stream: IC-905 10 GHz metadata differs from IC-9700/IC-705.
      if (!IcomRadioProfiles.SupportsAuthenticatedScope(
            RadioName, ExpectedScopeProfile))
        throw new NotSupportedException(
          $"Direct Icom LAN scope model mismatch: radio reports '{RadioName}' " +
          $"(CI-V 0x{RadioCivAddress:X2}), configured Scope model is " +
          $"'{ExpectedScopeProfile.Model}'. Select the matching model.");

      NegotiatedRxRate = AudioOptions.Receive.Enabled
        ? ChooseSampleRate(AudioOptions.Receive.SampleRate, RadioRxSampleCapabilities)
        : 0;
      // The microphone capture clock is configured to the operator's
      // requested rate. Do not silently negotiate another TX rate and
      // distort audio; leave TX audio inactive if the radio lacks that rate.
      uint selectedTx = (uint)AudioOptions.Transmit.SampleRate;
      NegotiatedTxRate = AudioOptions.Transmit.Enabled &&
        ChooseSampleRate(AudioOptions.Transmit.SampleRate,
          RadioTxSampleCapabilities) == selectedTx
          ? selectedTx : 0;

      if (AudioOptions.Receive.Enabled && NegotiatedRxRate == 0)
        Publish("Direct LAN: radio does not advertise supported RX audio sample rates.");
      if (AudioOptions.Transmit.Enabled && NegotiatedTxRate == 0)
        Publish("Direct LAN: radio does not advertise supported TX audio sample rates.");
      byte[] streamAuthId = AuthId.ToArray();
      ushort streamSequence = await SendStreamRequestAsync(
        control,
        LocalCivPort,
        localAudioPort,
        token);

      Publish(
        $"Direct Icom LAN: requesting correlated native CI-V stream on local UDP/{LocalCivPort}...");

      bool streamAccepted = false;
      DateTime streamDeadline = DateTime.UtcNow.AddSeconds(6);

      while (!streamAccepted && DateTime.UtcNow < streamDeadline)
      {
        byte[] packet = await ReceiveOneAsync(
          control,
          TimeSpan.FromMilliseconds(750),
          token);

        if (packet.Length == 0)
          continue;

        await HandleCommonPacketAsync(
          control,
          isControl: true,
          packet,
          token);

        if (IsMatchingStreamStatus(
              packet,
              ControlLocalSid,
              ControlRemoteSid,
              streamSequence,
              streamAuthId))
        {
          uint error = BinaryPrimitives.ReadUInt32LittleEndian(
            packet.AsSpan(0x30, 4));
          bool disconnected = packet[0x40] == 0x01;

          if (error != 0 || disconnected)
            throw new InvalidOperationException(
              $"Icom radio rejected the direct LAN media-stream request " +
              $"(error 0x{error:X8}, disconnected={disconnected}). " +
              "Close RS-BA1/other remote clients before starting experimental Direct LAN.");

          int civPort = BinaryPrimitives.ReadUInt16BigEndian(
            packet.AsSpan(0x42, 2));
          int audioPort = BinaryPrimitives.ReadUInt16BigEndian(
            packet.AsSpan(0x46, 2));

          if (civPort > 0)
          {
            RemoteCivPort = civPort;
            streamAccepted = true;
          }

          if (audioPort > 0)
            RemoteAudioPort = audioPort;
        }
        else if (IsMatchingStreamGrant(
                   packet,
                   ControlLocalSid,
                   ControlRemoteSid,
                   streamSequence,
                   streamAuthId))
        {
          streamAccepted = packet[0x60] == 0x01;
        }
      }

      if (!streamAccepted)
        throw new TimeoutException(
          "Icom radio did not return a correlated grant for the requested direct CI-V stream. " +
          "Close RS-BA1/other remote clients before retrying experimental Direct LAN.");

      control.Client.ReceiveTimeout = 1000;

      civ.Connect(RadioAddress, RemoteCivPort);
      await StartCommonHandshakeAsync(
        civ,
        isControl: false,
        token);

      await SendOpenCloseAsync(civ, close: false, token);
      await Task.Delay(80, token);

      IsStreaming = true;
      Publish(
        $"Direct Icom LAN native CI-V connected: " +
        $"{RadioAddress}:{RemoteCivPort} -> local UDP/{LocalCivPort}.");

      Task audioDrain = Task.CompletedTask;
      Task audioTransmit = Task.CompletedTask;
      if ((NegotiatedRxRate != 0 || NegotiatedTxRate != 0) && RemoteAudioPort > 0)
      {
        try
        {
          audio.Connect(RadioAddress, RemoteAudioPort);
          AudioRemoteSid = await StartAudioHandshakeAsync(audio, token);
          audioDrain = DrainAudioAsync(
            audio, (int)NegotiatedRxRate, token);
          AudioTxReady = NegotiatedTxRate != 0;
          if (AudioTxReady)
            audioTransmit = TransmitAudioAsync(audio, token);
        }
        catch (Exception ex)
        {
          // The dedicated optional audio socket may fail while CI-V is usable.
          // Log only the exception type: endpoint details can carry secrets.
          Log.Debug("Icom Direct LAN auxiliary audio unavailable ({Type})",
            ex.GetType().Name);
        }
      }

      await SendScopeConfigurationAsync(civ, token);
      if (PreferFastScopeSweep)
      {
        foreach (byte[] frame in BuildFastSweepCommands(
                   RadioCivAddress, ExpectedScopeProfile.ReceiverCount))
          await SendCivDataAsync(civ, frame, token);
        Publish("Direct Icom LAN: FAST scope sweep requested (radio-side setting).");
      }

      try
      {
        await RunStreamingLoopsAsync(
          control,
          civ,
          token);
      }
      finally
      {
        IsStreaming = false;
        AudioTxReady = false;
        TxAudio.Writer.TryComplete();

        try
        {
          await SendOpenCloseAsync(civ, close: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
          Log.Debug("Icom Direct LAN close handshake failed ({Type})", ex.GetType().Name);
        }

        try
        {
          _ = await SendAuthAsync(control, 0x01, CancellationToken.None);
        }
        catch (Exception ex)
        {
          Log.Debug("Icom Direct LAN signout failed ({Type})", ex.GetType().Name);
        }

        try
        {
          await Task.WhenAny(Task.WhenAll(audioDrain, audioTransmit), Task.Delay(150));
        }
        catch (Exception ex)
        {
          Log.Debug("Icom Direct LAN audio workers did not settle ({Type})", ex.GetType().Name);
        }
      }
    }

    private async Task RunStreamingLoopsAsync(
      UdpClient control,
      UdpClient civ,
      CancellationToken token)
    {
      Task<UdpReceiveResult> controlReceive = control.ReceiveAsync(token).AsTask();
      Task<UdpReceiveResult> civReceive = civ.ReceiveAsync(token).AsTask();
      using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
      Task<bool> tick = timer.WaitForNextTickAsync(token).AsTask();

      while (!token.IsCancellationRequested)
      {
        Task completed = await Task.WhenAny(
          controlReceive,
          civReceive,
          tick);

        if (completed == controlReceive)
        {
          UdpReceiveResult result = await controlReceive;
          DatagramCount++;
          await HandleControlPacketAsync(
            control,
            result.Buffer,
            token);

          controlReceive = control.ReceiveAsync(token).AsTask();
        }
        else if (completed == civReceive)
        {
          UdpReceiveResult result = await civReceive;
          DatagramCount++;
          await HandleCivPacketAsync(
            civ,
            result.Buffer,
            token);

          civReceive = civ.ReceiveAsync(token).AsTask();
        }
        else
        {
          if (!await tick)
            break;

          await MaintenanceTickAsync(
            control,
            civ,
            token);

          tick = timer.WaitForNextTickAsync(token).AsTask();
        }
      }
    }

    private async Task MaintenanceTickAsync(
      UdpClient control,
      UdpClient civ,
      CancellationToken token)
    {
      DateTime now = DateTime.UtcNow;

      if ((now - LastControlIdleUtc).TotalMilliseconds >= 1000)
      {
        await SendIdleAsync(
          control,
          isControl: true,
          token);
        LastControlIdleUtc = now;
      }

      if ((now - LastCivIdleUtc).TotalMilliseconds >= 1000)
      {
        await SendIdleAsync(
          civ,
          isControl: false,
          token);
        LastCivIdleUtc = now;
      }

      if ((now - LastControlPingUtc).TotalSeconds >= 3)
      {
        await SendPingAsync(
          control,
          isControl: true,
          ControlPingSeq++,
          replyId: null,
          token);
        LastControlPingUtc = now;
      }

      if ((now - LastCivPingUtc).TotalSeconds >= 3)
      {
        await SendPingAsync(
          civ,
          isControl: false,
          CivPingSeq++,
          replyId: null,
          token);
        LastCivPingUtc = now;
      }

      // The radio silently expires media ownership if authentication renewal is
      // not acknowledged. Track the request, not merely the send time: an
      // unrelated/stale token reply must not extend this session's lifetime.
      if (PendingRenewalInnerSequence.HasValue)
      {
        if ((now - LastAuthUtc).TotalSeconds >= 50)
          throw new TimeoutException(
            "Direct Icom LAN authentication renewal was not acknowledged.");

        if ((now - LastAuthRequestUtc).TotalMilliseconds >= 2500)
        {
          PendingRenewalAuthId = AuthId.ToArray();
          PendingRenewalInnerSequence =
            await SendAuthAsync(control, 0x05, token);
          LastAuthRequestUtc = now;
        }
      }
      else if ((now - LastAuthUtc).TotalSeconds >= 20)
      {
        PendingRenewalAuthId = AuthId.ToArray();
        PendingRenewalInnerSequence =
          await SendAuthAsync(control, 0x05, token);
        LastAuthRequestUtc = now;
      }

      if (AudioOptions.Meters.Enabled &&
          (now - LastMeterPollUtc).TotalMilliseconds >=
            Math.Clamp(AudioOptions.Meters.PollIntervalMs, 100, 2000) &&
          (!PendingMeter.HasValue ||
            (now - PendingMeterUtc).TotalMilliseconds >= 800))
      {
        IcomMeterKind[] meters = EnabledMeters(AudioOptions.Meters);
        if (meters.Length != 0)
        {
          IcomMeterKind next = meters[NextMeterIndex++ % meters.Length];
          await SendCivDataAsync(civ,
            IcomMeterCivCodec.BuildRead(RadioCivAddress, next), token);
          PendingMeter = next;
          PendingMeterUtc = now;
          LastMeterPollUtc = now;
        }
      }

      DateTime lastSweepUtc = new DateTime(
        Math.Max(0, Interlocked.Read(ref LastNativeScopeTicks)),
        DateTimeKind.Utc);
      if (IcomScopeRecoveryPolicy.ShouldReassert(
            now, LastScopeCommandUtc, lastSweepUtc))
        await SendScopeConfigurationAsync(civ, token);
    }

    private async Task HandleControlPacketAsync(
      UdpClient control,
      byte[] packet,
      CancellationToken token)
    {
      await HandleCommonPacketAsync(
        control,
        isControl: true,
        packet,
        token);

      if (PendingRenewalInnerSequence.HasValue &&
          IsMatchingAuthReply(
            packet,
            ControlLocalSid,
            ControlRemoteSid,
            0x05,
            PendingRenewalInnerSequence.Value,
            PendingRenewalAuthId))
      {
        uint response = BinaryPrimitives.ReadUInt32LittleEndian(
          packet.AsSpan(0x30, 4));

        if (response != 0)
          throw new InvalidOperationException(
            $"Icom radio rejected Direct LAN authentication renewal " +
            $"(0x{response:X8}).");

        AuthId = packet.AsSpan(0x1A, 6).ToArray();
        PendingRenewalInnerSequence = null;
        PendingRenewalAuthId = Array.Empty<byte>();
        LastAuthUtc = DateTime.UtcNow;
        return;
      }

      if (packet.Length == StatusSize &&
          MatchesControlEnvelope(
            packet,
            ControlLocalSid,
            ControlRemoteSid) &&
          packet.AsSpan(0x30, 3).SequenceEqual(
            new byte[] { 0xFF, 0xFF, 0xFF }))
        Publish(
          "Direct Icom LAN: correlated control session reported a media-stream error.");
    }

    private async Task HandleCivPacketAsync(
      UdpClient civ,
      byte[] packet,
      CancellationToken token)
    {
      await HandleCommonPacketAsync(
        civ,
        isControl: false,
        packet,
        token);

      if (packet.Length < 16 ||
          BinaryPrimitives.ReadUInt16LittleEndian(
            packet.AsSpan(4, 2)) != 0 ||
          !MatchesControlEnvelope(packet, CivLocalSid, CivRemoteSid))
        return;

      // Idle and open/close are tracked packets too. Include them in the
      // transport sequence baseline or ordinary keepalives look like loss.
      ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(
        packet.AsSpan(6, 2));
      if (!CivRxSequences.Accept(sequence) ||
          packet.Length < 22 || packet[16] != 0xC1)
        return;

      int declaredLength =
        packet[17] |
        (packet[18] << 8);

      if (declaredLength <= 0 ||
          packet.Length != 21 + declaredLength)
        return;

      CivPayloadCount++;

      byte[] payload = packet
        .AsSpan(21, declaredLength)
        .ToArray();

      // Only the active request and the authenticated radio's address
      // can update a meter; the high-rate 27 00 Scope stream stays independent.
      if (PendingMeter.HasValue)
      {
        for (int offset = 0; offset + 9 <= payload.Length; offset++)
        {
          if (IcomMeterCivCodec.TryParse(
                payload.AsSpan(offset, 9), RadioCivAddress,
                out IcomMeterReading reading) &&
              reading.Kind == PendingMeter.Value)
          {
            PendingMeter = null;
            MeterReceived?.Invoke(reading);
            break;
          }
        }
      }
      CivDataReceived?.Invoke(payload);
    }

    private static IcomMeterKind[] EnabledMeters(IcomDirectLanMeterSettings options)
    {
      var kinds = new List<IcomMeterKind>(4);
      if (options.S) kinds.Add(IcomMeterKind.S);
      if (options.Power) kinds.Add(IcomMeterKind.Power);
      if (options.Alc) kinds.Add(IcomMeterKind.Alc);
      if (options.Swr) kinds.Add(IcomMeterKind.Swr);
      return kinds.ToArray();
    }

    private async Task HandleCommonPacketAsync(
      UdpClient client,
      bool isControl,
      byte[] packet,
      CancellationToken token)
    {
      if (packet.Length < 16)
        return;

      ushort type = BinaryPrimitives.ReadUInt16LittleEndian(
        packet.AsSpan(4, 2));

      if (packet.Length == 21 && type == 0x0007)
      {
        if (packet[16] == 0x00)
        {
          ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(
            packet.AsSpan(6, 2));
          byte[] replyId = packet.AsSpan(17, 4).ToArray();

          await SendPingAsync(
            client,
            isControl,
            seq,
            replyId,
            token);
        }

        return;
      }

      if (type != 0x0001)
        return;

      if (packet.Length == 16)
      {
        ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(
          packet.AsSpan(6, 2));
        await RetransmitAsync(
          client,
          isControl,
          seq,
          seq,
          token);
        return;
      }

      for (int offset = 16;
           offset + 3 < packet.Length;
           offset += 4)
      {
        ushort first = BinaryPrimitives.ReadUInt16LittleEndian(
          packet.AsSpan(offset, 2));
        ushort last = BinaryPrimitives.ReadUInt16LittleEndian(
          packet.AsSpan(offset + 2, 2));

        await RetransmitAsync(
          client,
          isControl,
          first,
          last,
          token);
      }
    }

    private async Task StartCommonHandshakeAsync(
      UdpClient client,
      bool isControl,
      CancellationToken token)
    {
      uint localSid = isControl
        ? ControlLocalSid
        : CivLocalSid;

      await SendRawAsync(
        client,
        BuildControlPacket(
          0x0003,
          0,
          localSid,
          0),
        isControl,
        tracked: false,
        token);
      await SendRawAsync(
        client,
        BuildControlPacket(
          0x0003,
          0,
          localSid,
          0),
        isControl,
        tracked: false,
        token);

      byte[] here = await ReceiveMatchingAsync(
        client,
        packet =>
          packet.Length == 16 &&
          BinaryPrimitives.ReadUInt16LittleEndian(
            packet.AsSpan(4, 2)) == 0x0004 &&
          BinaryPrimitives.ReadUInt32BigEndian(
            packet.AsSpan(12, 4)) == localSid,
        TimeSpan.FromSeconds(2),
        token);

      uint remoteSid = BinaryPrimitives.ReadUInt32BigEndian(
        here.AsSpan(8, 4));

      if (remoteSid == 0)
        throw new InvalidDataException(
          "Icom LAN handshake returned an invalid remote session ID.");

      if (isControl)
        ControlRemoteSid = remoteSid;
      else
        CivRemoteSid = remoteSid;

      byte[] ready = BuildControlPacket(
        0x0006,
        1,
        localSid,
        remoteSid);

      await SendRawAsync(
        client,
        ready,
        isControl,
        tracked: false,
        token);
      await SendRawAsync(
        client,
        ready,
        isControl,
        tracked: false,
        token);

      _ = await ReceiveMatchingAsync(
        client,
        packet =>
          packet.Length == 16 &&
          BinaryPrimitives.ReadUInt16LittleEndian(
            packet.AsSpan(4, 2)) == 0x0006 &&
          BinaryPrimitives.ReadUInt32BigEndian(
            packet.AsSpan(8, 4)) == remoteSid &&
          BinaryPrimitives.ReadUInt32BigEndian(
            packet.AsSpan(12, 4)) == localSid,
        TimeSpan.FromSeconds(2),
        token);
    }

    private async Task<(ushort InnerSequence, ushort TokenRequest)> SendLoginAsync(
      UdpClient control,
      CancellationToken token)
    {
      ushort innerSequence = AuthInnerSeq++;

      Span<byte> requestBytes = stackalloc byte[2];
      RandomNumberGenerator.Fill(requestBytes);
      ushort tokenRequest =
        BinaryPrimitives.ReadUInt16LittleEndian(requestBytes);
      if (tokenRequest == 0)
        tokenRequest = 1;

      byte[] packet = BuildLoginPacket(
        ControlLocalSid,
        ControlRemoteSid,
        innerSequence,
        tokenRequest,
        Username,
        Password,
        ClientName);

      await SendTrackedAsync(
        control,
        isControl: true,
        packet,
        token);

      return (innerSequence, tokenRequest);
    }

    private async Task<ushort> SendAuthAsync(
      UdpClient control,
      byte kind,
      CancellationToken token)
    {
      ushort innerSequence = AuthInnerSeq++;

      byte[] packet = BuildAuthPacket(
        ControlLocalSid,
        ControlRemoteSid,
        innerSequence,
        kind,
        AuthId);

      await SendTrackedAsync(
        control,
        isControl: true,
        packet,
        token);

      return innerSequence;
    }

    private async Task<ushort> SendStreamRequestAsync(
      UdpClient control,
      int localCivPort,
      int localAudioPort,
      CancellationToken token)
    {
      ushort innerSequence = AuthInnerSeq++;

      byte[] packet = BuildStreamRequestPacket(
        ControlLocalSid,
        ControlRemoteSid,
        innerSequence,
        AuthId,
        RadioName,
        Username,
        RadioCommonCapability,
        RadioGuid,
        RadioMacAddress,
        RadioRxSampleCapabilities,
        localCivPort,
        localAudioPort,
        (byte)AudioOptions.Receive.Codec,
        NegotiatedRxRate,
        (byte)AudioOptions.Transmit.Codec,
        NegotiatedTxRate,
        (uint)Math.Clamp(AudioOptions.Transmit.PrebufferMs, 20, 500),
        rxEnabled: AudioOptions.Receive.Enabled);

      await SendTrackedAsync(
        control,
        isControl: true,
        packet,
        token);

      return innerSequence;
    }

    internal static byte[] BuildLoginPacket(
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      ushort tokenRequest,
      string username,
      string password,
      string clientName)
    {
      byte[] packet = new byte[LoginSize];
      BinaryPrimitives.WriteUInt32LittleEndian(
        packet.AsSpan(0, 4),
        LoginSize);
      WriteSid(packet, 8, localSid);
      WriteSid(packet, 12, remoteSid);

      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x10, 4),
        LoginSize - 0x10);
      packet[0x14] = 0x01;
      packet[0x15] = 0x00;
      BinaryPrimitives.WriteUInt16BigEndian(
        packet.AsSpan(0x16, 2),
        innerSequence);
      BinaryPrimitives.WriteUInt16LittleEndian(
        packet.AsSpan(0x1A, 2),
        tokenRequest);

      EncodePasscode(username).CopyTo(packet, 0x40);
      EncodePasscode(password).CopyTo(packet, 0x50);
      WriteAsciiZ(
        packet.AsSpan(0x60, 16),
        NormalizeClientName(clientName));
      return packet;
    }

    internal static byte[] BuildAuthPacket(
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      byte kind,
      ReadOnlySpan<byte> authId)
    {
      if (authId.Length != 6)
        throw new ArgumentException(
          "Icom LAN authentication ID must be exactly 6 bytes.",
          nameof(authId));

      byte[] packet = new byte[AuthSize];
      BinaryPrimitives.WriteUInt32LittleEndian(
        packet.AsSpan(0, 4),
        AuthSize);
      WriteSid(packet, 8, localSid);
      WriteSid(packet, 12, remoteSid);

      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x10, 4),
        AuthSize - 0x10);
      packet[0x14] = 0x01;
      packet[0x15] = kind;
      BinaryPrimitives.WriteUInt16BigEndian(
        packet.AsSpan(0x16, 2),
        innerSequence);
      authId.CopyTo(packet.AsSpan(0x1A, 6));
      return packet;
    }

    internal static byte[] BuildStreamRequestPacket(
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      ReadOnlySpan<byte> authId,
      string radioName,
      string username,
      ushort commonCapability,
      ReadOnlySpan<byte> radioGuid,
      ReadOnlySpan<byte> radioMac,
      ushort rxSampleCapabilities,
      int localCivPort,
      int localAudioPort,
      byte rxCodec = Lpcm16MonoCodec,
      uint rxSampleRate = 0,
      byte txCodec = 0,
      uint txSampleRate = 0,
      uint txBufferMs = 0,
      bool rxEnabled = true)
    {
      if (authId.Length != 6)
        throw new ArgumentException(
          "Icom LAN authentication ID must be exactly 6 bytes.",
          nameof(authId));
      if (radioGuid.Length != 16)
        throw new ArgumentException(
          "Icom LAN radio GUID must be exactly 16 bytes.",
          nameof(radioGuid));
      if (radioMac.Length != 6)
        throw new ArgumentException(
          "Icom LAN radio MAC must be exactly 6 bytes.",
          nameof(radioMac));

      byte[] packet = new byte[ConnInfoSize];
      BinaryPrimitives.WriteUInt32LittleEndian(
        packet.AsSpan(0, 4),
        ConnInfoSize);
      WriteSid(packet, 8, localSid);
      WriteSid(packet, 12, remoteSid);

      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x10, 4),
        ConnInfoSize - 0x10);
      packet[0x14] = 0x01;
      packet[0x15] = 0x03;
      BinaryPrimitives.WriteUInt16BigEndian(
        packet.AsSpan(0x16, 2),
        innerSequence);
      authId.CopyTo(packet.AsSpan(0x1A, 6));

      if (commonCapability == MacIdentityCapability)
      {
        BinaryPrimitives.WriteUInt16BigEndian(
          packet.AsSpan(0x27, 2),
          MacIdentityCapability);
        radioMac.CopyTo(packet.AsSpan(0x2A, 6));
      }
      else
      {
        radioGuid.CopyTo(packet.AsSpan(0x20, 16));
      }

      WriteAsciiZ(packet.AsSpan(0x40, 32), radioName);
      EncodePasscode(username).CopyTo(packet, 0x60);

      // Legacy test callers retain receive-only default negotiation.
      uint receiveSampleRate = !rxEnabled ? 0
        : rxSampleRate != 0 ? rxSampleRate
        : SelectReceiveSampleRate(rxSampleCapabilities);
      bool receiveAudio = receiveSampleRate != 0;
      bool transmitAudio = txSampleRate != 0;

      packet[0x70] = receiveAudio ? (byte)0x01 : (byte)0x00;
      packet[0x71] = transmitAudio ? (byte)0x01 : (byte)0x00;
      packet[0x72] = receiveAudio ? rxCodec : (byte)0x00;
      packet[0x73] = transmitAudio ? txCodec : (byte)0x00;

      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x74, 4), receiveSampleRate);
      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x78, 4), txSampleRate);
      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x7C, 4),
        checked((uint)localCivPort));
      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x80, 4),
        checked((uint)localAudioPort));
      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(0x84, 4), txBufferMs);
      packet[0x88] = 0x01;
      return packet;
    }

    internal static uint SelectReceiveSampleRate(
      ushort capabilityMask)
    {
      // Prefer the 48 kHz LPCM path verified on the IC-9700, then fall back
      // only to rates explicitly advertised by the RS-BA1 capability bit set.
      if ((capabilityMask & 0x0008) != 0) return 48000;
      if ((capabilityMask & 0x0004) != 0) return 32000;
      if ((capabilityMask & 0x0002) != 0) return 16000;
      if ((capabilityMask & 0x0080) != 0) return 12000;
      if ((capabilityMask & 0x0040) != 0) return 44100;
      if ((capabilityMask & 0x0020) != 0) return 22050;
      if ((capabilityMask & 0x0010) != 0) return 11025;
      if ((capabilityMask & 0x0001) != 0) return 8000;
      return 0;
    }

    internal static uint ChooseSampleRate(
      IcomLanAudioSampleRate requested, ushort advertised)
    {
      int value = (int)requested;
      ushort bit = value switch
      {
        48000 => 0x0008,
        32000 => 0x0004,
        16000 => 0x0002,
        12000 => 0x0080,
        44100 => 0x0040,
        22050 => 0x0020,
        11025 => 0x0010,
        8000 => 0x0001,
        _ => (ushort)0
      };
      return bit != 0 && (advertised & bit) != 0
        ? (uint)value : SelectReceiveSampleRate(advertised);
    }

    private async Task SendOpenCloseAsync(
      UdpClient civ,
      bool close,
      CancellationToken token)
    {
      byte[] packet = new byte[22];
      packet[0] = 0x16;

      WriteSid(packet, 8, CivLocalSid);
      WriteSid(packet, 12, CivRemoteSid);

      packet[16] = 0xC0;
      packet[17] = 0x01;
      packet[19] = (byte)(CivInnerSeq >> 8);
      packet[20] = (byte)CivInnerSeq;
      packet[21] = close
        ? (byte)0x00
        : (byte)0x05;

      CivInnerSeq++;

      await SendTrackedAsync(
        civ,
        isControl: false,
        packet,
        token);
    }

    private async Task SendScopeConfigurationAsync(
      UdpClient civ,
      CancellationToken token)
    {
      // Direct LAN periodically reasserts only the minimum commands needed
      // to keep scope waveform output alive. Do not force MAIN/SUB sweep speed
      // (or any other operator scope preference) from the transport layer.
      byte[][] commands = BuildScopeEnableCommands(RadioCivAddress);

      foreach (byte[] command in commands)
      {
        await SendCivDataAsync(
          civ,
          command,
          token);

        await Task.Delay(8, token);
      }

      LastScopeCommandUtc = DateTime.UtcNow;
    }

    // Preserves the authenticated device's actual CI-V address, including
    // operator-customized addresses. It does not assume IC-9700 0xA2.
    internal static byte[][] BuildScopeEnableCommands(byte civAddress) =>
    [
      [0xFE, 0xFE, civAddress, 0xE0, 0x27, 0x10, 0x01, 0xFD],
      [0xFE, 0xFE, civAddress, 0xE0, 0x27, 0x11, 0x01, 0xFD]
    ];

    internal static byte[][] BuildFastSweepCommands(
      byte civAddress, int receiverCount) =>
      Enumerable.Range(0, receiverCount)
        .Select(index => new byte[]
        {
          0xFE, 0xFE, civAddress, 0xE0, 0x27, 0x1A,
          (byte)index, 0x00, 0xFD
        })
        .ToArray();

    private async Task SendCivDataAsync(
      UdpClient civ,
      byte[] data,
      CancellationToken token)
    {
      byte[] packet = new byte[21 + data.Length];

      BinaryPrimitives.WriteUInt32LittleEndian(
        packet.AsSpan(0, 4),
        (uint)packet.Length);

      WriteSid(packet, 8, CivLocalSid);
      WriteSid(packet, 12, CivRemoteSid);

      packet[16] = 0xC1;
      BinaryPrimitives.WriteUInt16LittleEndian(
        packet.AsSpan(17, 2),
        (ushort)data.Length);
      BinaryPrimitives.WriteUInt16BigEndian(
        packet.AsSpan(19, 2),
        CivInnerSeq++);

      data.CopyTo(packet, 21);

      await SendTrackedAsync(
        civ,
        isControl: false,
        packet,
        token);
    }

    private async Task SendIdleAsync(
      UdpClient client,
      bool isControl,
      CancellationToken token)
    {
      uint localSid = isControl
        ? ControlLocalSid
        : CivLocalSid;
      uint remoteSid = isControl
        ? ControlRemoteSid
        : CivRemoteSid;

      byte[] packet = BuildControlPacket(
        0x0000,
        0,
        localSid,
        remoteSid);

      await SendTrackedAsync(
        client,
        isControl,
        packet,
        token);
    }

    private async Task SendPingAsync(
      UdpClient client,
      bool isControl,
      ushort sequence,
      byte[]? replyId,
      CancellationToken token)
    {
      uint localSid = isControl
        ? ControlLocalSid
        : CivLocalSid;
      uint remoteSid = isControl
        ? ControlRemoteSid
        : CivRemoteSid;

      byte[] packet = new byte[21];
      packet[0] = 0x15;
      packet[4] = 0x07;
      BinaryPrimitives.WriteUInt16LittleEndian(
        packet.AsSpan(6, 2),
        sequence);
      WriteSid(packet, 8, localSid);
      WriteSid(packet, 12, remoteSid);

      if (replyId == null)
      {
        packet[16] = 0x00;
        packet[17] = RandomNumberGenerator.GetBytes(1)[0];
        packet[18] = (byte)PingInnerSeq;
        packet[19] = (byte)(PingInnerSeq >> 8);
        packet[20] = 0x06;
        PingInnerSeq++;
      }
      else
      {
        packet[16] = 0x01;
        replyId.AsSpan(0, Math.Min(4, replyId.Length))
          .CopyTo(packet.AsSpan(17, 4));
      }

      await SendRawAsync(
        client,
        packet,
        isControl,
        tracked: false,
        token);
    }

    private async Task SendTrackedAsync(
      UdpClient client,
      bool isControl,
      byte[] packet,
      CancellationToken token)
    {
      ushort sequence = isControl
        ? ControlOuterSeq++
        : CivOuterSeq++;

      BinaryPrimitives.WriteUInt16LittleEndian(
        packet.AsSpan(6, 2),
        sequence);

      RememberTx(
        isControl,
        sequence,
        packet);

      await SendRawAsync(
        client,
        packet,
        isControl,
        tracked: true,
        token);
    }

    private async Task SendRawAsync(
      UdpClient client,
      byte[] packet,
      bool isControl,
      bool tracked,
      CancellationToken token)
    {
      SemaphoreSlim gate = isControl
        ? ControlSendLock
        : CivSendLock;

      await gate.WaitAsync(token);
      try
      {
        await client.SendAsync(
          packet,
          token);
      }
      finally
      {
        gate.Release();
      }
    }

    private void RememberTx(
      bool isControl,
      ushort sequence,
      byte[] packet)
    {
      Dictionary<ushort, byte[]> history =
        isControl
          ? ControlTxHistory
          : CivTxHistory;
      Queue<ushort> order =
        isControl
          ? ControlTxOrder
          : CivTxOrder;

      history[sequence] = packet.ToArray();
      order.Enqueue(sequence);

      while (order.Count > 256)
      {
        ushort old = order.Dequeue();
        history.Remove(old);
      }
    }

    private async Task RetransmitAsync(
      UdpClient client,
      bool isControl,
      ushort first,
      ushort last,
      CancellationToken token)
    {
      Dictionary<ushort, byte[]> history =
        isControl
          ? ControlTxHistory
          : CivTxHistory;

      ushort current = first;

      for (int count = 0; count < 64; count++)
      {
        if (history.TryGetValue(
              current,
              out byte[]? packet))
        {
          await SendRawAsync(
            client,
            packet,
            isControl,
            tracked: false,
            token);
        }
        else
        {
          uint localSid = isControl
            ? ControlLocalSid
            : CivLocalSid;
          uint remoteSid = isControl
            ? ControlRemoteSid
            : CivRemoteSid;

          byte[] idle = BuildControlPacket(
            0x0000,
            current,
            localSid,
            remoteSid);

          await SendRawAsync(
            client,
            idle,
            isControl,
            tracked: false,
            token);
        }

        if (current == last)
          break;

        current++;
      }
    }

    private static byte[] BuildControlPacket(
      ushort type,
      ushort sequence,
      uint localSid,
      uint remoteSid)
    {
      byte[] packet = new byte[16];
      packet[0] = 0x10;

      BinaryPrimitives.WriteUInt16LittleEndian(
        packet.AsSpan(4, 2),
        type);
      BinaryPrimitives.WriteUInt16LittleEndian(
        packet.AsSpan(6, 2),
        sequence);

      WriteSid(packet, 8, localSid);
      WriteSid(packet, 12, remoteSid);
      return packet;
    }

    private static async Task<byte[]> ReceiveMatchingAsync(
      UdpClient client,
      Func<byte[], bool> predicate,
      TimeSpan timeout,
      CancellationToken token)
    {
      DateTime deadline = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < deadline)
      {
        TimeSpan remaining = deadline - DateTime.UtcNow;
        byte[] packet = await ReceiveOneAsync(
          client,
          remaining,
          token);

        if (packet.Length == 0)
          break;

        if (predicate(packet))
          return packet;
      }

      throw new TimeoutException(
        "Timed out waiting for the Icom LAN handshake.");
    }

    private static async Task<byte[]> ReceiveOneAsync(
      UdpClient client,
      TimeSpan timeout,
      CancellationToken token)
    {
      using var timeoutCts =
        CancellationTokenSource.CreateLinkedTokenSource(token);
      timeoutCts.CancelAfter(timeout);

      try
      {
        UdpReceiveResult result =
          await client.ReceiveAsync(timeoutCts.Token);
        return result.Buffer;
      }
      catch (OperationCanceledException)
        when (!token.IsCancellationRequested)
      {
        return Array.Empty<byte>();
      }
    }

    private async Task<uint> StartAudioHandshakeAsync(
      UdpClient audio, CancellationToken token)
    {
      byte[] probe = BuildControlPacket(0x0003, 0, AudioLocalSid, 0);
      await SendRawAsync(audio, probe, false, false, token);
      await SendRawAsync(audio, probe, false, false, token);

      byte[] here = await ReceiveMatchingAsync(
        audio,
        packet => packet.Length == 16 &&
          BinaryPrimitives.ReadUInt16LittleEndian(
            packet.AsSpan(4, 2)) == 0x0004 &&
          BinaryPrimitives.ReadUInt32BigEndian(
            packet.AsSpan(12, 4)) == AudioLocalSid,
        TimeSpan.FromSeconds(2), token);

      uint remoteSid = BinaryPrimitives.ReadUInt32BigEndian(
        here.AsSpan(8, 4));
      if (remoteSid == 0)
        throw new InvalidDataException(
          "Icom LAN audio handshake returned an invalid remote SID.");

      byte[] ready = BuildControlPacket(
        0x0006, 1, AudioLocalSid, remoteSid);
      await SendRawAsync(audio, ready, false, false, token);
      await SendRawAsync(audio, ready, false, false, token);
      _ = await ReceiveMatchingAsync(
        audio,
        packet => packet.Length == 16 &&
          BinaryPrimitives.ReadUInt16LittleEndian(
            packet.AsSpan(4, 2)) == 0x0006 &&
          BinaryPrimitives.ReadUInt32BigEndian(
            packet.AsSpan(8, 4)) == remoteSid &&
          BinaryPrimitives.ReadUInt32BigEndian(
            packet.AsSpan(12, 4)) == AudioLocalSid,
        TimeSpan.FromSeconds(2), token);

      return remoteSid;
    }

    private async Task DrainAudioAsync(
      UdpClient audio,
      int sampleRate,
      CancellationToken token)
    {
      while (!token.IsCancellationRequested)
      {
        try
        {
          // Audio has its own independent keepalive cycle. A muted or
          // squelched receiver must not silently expire the UDP session.
          using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
          timeout.CancelAfter(TimeSpan.FromSeconds(5));
          UdpReceiveResult received;
          try
          {
            received = await audio.ReceiveAsync(timeout.Token);
          }
          catch (OperationCanceledException) when (!token.IsCancellationRequested)
          {
            await SendAudioPingAsync(audio, token);
            continue;
          }

          byte[] packet = received.Buffer;
          if (packet.Length == 21 &&
              BinaryPrimitives.ReadUInt16LittleEndian(
                packet.AsSpan(4, 2)) == 0x0007 &&
              BinaryPrimitives.ReadUInt32BigEndian(
                packet.AsSpan(8, 4)) == AudioRemoteSid &&
              BinaryPrimitives.ReadUInt32BigEndian(
                packet.AsSpan(12, 4)) == AudioLocalSid &&
              packet[16] == 0x00)
          {
            byte[] reply = packet.ToArray();
            reply[16] = 0x01;
            WriteSid(reply, 8, AudioLocalSid);
            WriteSid(reply, 12, AudioRemoteSid);
            await SendRawAsync(audio, reply, false, false, token);
            continue;
          }

          if (packet.Length == 16 &&
              BinaryPrimitives.ReadUInt16LittleEndian(
                packet.AsSpan(4, 2)) == 0x0001 &&
              MatchesControlEnvelope(packet, AudioLocalSid, AudioRemoteSid))
          {
            if (AudioOptions.RetransmitAudio)
            {
              ushort requested = BinaryPrimitives.ReadUInt16LittleEndian(
                packet.AsSpan(6, 2));
              byte[]? original;
              lock (AudioHistoryLock)
                AudioTxHistory.TryGetValue(requested, out original);
              if (original != null && IsPttActive?.Invoke() == true)
                await SendAudioRawAsync(audio, original, token);
            }
            continue;
          }

          if (sampleRate > 0 && AudioOptions.Receive.Enabled &&
              IcomLanLpcmAudioCodec.TryDecode(
                packet, AudioRemoteSid, AudioLocalSid,
                AudioOptions.Receive.Codec, out var pcm))
          {
            try
            {
              AudioFrameReceived?.Invoke(sampleRate, pcm);
            }
            catch (Exception ex)
            {
              Log.Warning(ex, "Icom LAN audio consumer rejected a PCM frame");
            }
          }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
          break;
        }
        catch (SocketException ex)
        {
          if (!token.IsCancellationRequested)
            Log.Warning(ex, "Icom LAN audio socket stopped");
          break;
        }
        catch (Exception ex)
        {
          // An invalid/closed audio endpoint should never form a hot loop
          // or bring down the independently authenticated CAT/Scope session.
          Log.Warning(ex, "Icom LAN audio receive failed");
          break;
        }
      }
    }

    private async Task TransmitAudioAsync(
      UdpClient audio, CancellationToken token)
    {
      int bytesPerSample = AudioOptions.Transmit.Codec ==
        IcomLanAudioCodec.Lpcm16Mono ? 2 : 1;
      int maxSamples = 1364 / bytesPerSample;
      await foreach (float[] captured in TxAudio.Reader.ReadAllAsync(token))
      {
        if (!IsStreaming || IsPttActive?.Invoke() != true)
          continue;
        for (int offset = 0; offset < captured.Length; offset += maxSamples)
        {
          if (!IsStreaming || IsPttActive?.Invoke() != true) break;
          int length = Math.Min(maxSamples, captured.Length - offset);
          var gain = Math.Clamp(AudioOptions.Transmit.MicrophoneGain, 0, 200) / 100f;
          var chunk = new float[length];
          for (int i = 0; i < length; i++)
            chunk[i] = Math.Clamp(captured[offset + i] * gain, -1f, 1f);

          ushort sequence = AudioOuterSeq++;
          byte[] datagram = IcomLanLpcmAudioCodec.EncodeTransmit(
            chunk, AudioLocalSid, AudioRemoteSid,
            sequence, AudioPayloadSeq++, AudioOptions.Transmit.Codec);

          if (AudioOptions.RetransmitAudio)
          {
            lock (AudioHistoryLock)
            {
              AudioTxHistory[sequence] = datagram;
              AudioTxOrder.Enqueue(sequence);
              while (AudioTxOrder.Count > 256)
                AudioTxHistory.Remove(AudioTxOrder.Dequeue());
            }
          }
          await SendAudioRawAsync(audio, datagram, token);
        }
      }
    }

    private async Task SendAudioRawAsync(
      UdpClient audio, byte[] datagram, CancellationToken token)
    {
      await AudioSendLock.WaitAsync(token);
      try { await audio.SendAsync(datagram, token); }
      finally { AudioSendLock.Release(); }
    }

    private async Task SendAudioPingAsync(
      UdpClient audio, CancellationToken token)
    {
      byte[] ping = new byte[21];
      BinaryPrimitives.WriteUInt32LittleEndian(ping.AsSpan(0, 4), 21);
      BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(4, 2), 0x0007);
      BinaryPrimitives.WriteUInt16LittleEndian(
        ping.AsSpan(6, 2), AudioPingSeq++);
      WriteSid(ping, 8, AudioLocalSid);
      WriteSid(ping, 12, AudioRemoteSid);
      ping[17] = RandomNumberGenerator.GetBytes(1)[0];
      ping[18] = (byte)AudioPingInnerSeq;
      ping[19] = (byte)(AudioPingInnerSeq >> 8);
      ping[20] = 0x06;
      AudioPingInnerSeq++;
      await SendAudioRawAsync(audio, ping, token);
    }

    private void ParseCapabilities(byte[] packet)
    {
      int count = (
        packet.Length - CapabilitiesHeaderSize) /
        RadioCapabilitySize;

      if (count <= 0)
        throw new InvalidDataException(
          "Icom radio returned an empty capabilities packet.");

      int selected = -1;
      for (int i = 0; i < count; i++)
      {
        int offset =
          CapabilitiesHeaderSize + i * RadioCapabilitySize;
        byte civ = packet[offset + 0x52];

        if (civ > 0 && civ < 0xE0)
        {
          selected = offset;
          break;
        }
      }

      if (selected < 0)
        throw new InvalidDataException(
          "Icom LAN capabilities did not contain a usable CI-V radio.");

      packet.AsSpan(selected, 16)
        .CopyTo(RadioGuid);
      packet.AsSpan(selected + 0x0A, 6)
        .CopyTo(RadioMacAddress);

      RadioCommonCapability =
        BinaryPrimitives.ReadUInt16BigEndian(
          packet.AsSpan(selected + 0x07, 2));

      string name = ReadAsciiZ(
        packet.AsSpan(selected + 0x10, 32));
      if (!string.IsNullOrWhiteSpace(name))
        RadioName = name;

      RadioAudioName = ReadAsciiZ(
        packet.AsSpan(selected + 0x30, 32));
      RadioCivAddress = packet[selected + 0x52];
      RadioRxSampleCapabilities =
        BinaryPrimitives.ReadUInt16BigEndian(
          packet.AsSpan(selected + 0x53, 2));
      RadioTxSampleCapabilities =
        BinaryPrimitives.ReadUInt16BigEndian(
          packet.AsSpan(selected + 0x55, 2));
      RadioBaudRate =
        BinaryPrimitives.ReadUInt32BigEndian(
          packet.AsSpan(selected + 0x5A, 4));

      string identity =
        RadioCommonCapability == MacIdentityCapability
          ? $"MAC {BitConverter.ToString(RadioMacAddress)}"
          : "GUID";

      Publish(
        $"Direct Icom LAN capabilities: audio '{RadioAudioName}', " +
        $"identity {identity}, RX 0x{RadioRxSampleCapabilities:X4}, " +
        $"TX 0x{RadioTxSampleCapabilities:X4}.");
    }

    internal static bool MatchesControlEnvelope(
      ReadOnlySpan<byte> packet,
      uint localSid,
      uint remoteSid)
    {
      if (packet.Length < 16)
        return false;

      return
        BinaryPrimitives.ReadUInt32BigEndian(
          packet.Slice(0x08, 4)) == remoteSid &&
        BinaryPrimitives.ReadUInt32BigEndian(
          packet.Slice(0x0C, 4)) == localSid;
    }

    internal static bool IsMatchingLoginReply(
      ReadOnlySpan<byte> packet,
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      ushort tokenRequest)
    {
      return
        packet.Length == LoginResponseSize &&
        BinaryPrimitives.ReadUInt32LittleEndian(
          packet.Slice(0, 4)) == LoginResponseSize &&
        BinaryPrimitives.ReadUInt16LittleEndian(
          packet.Slice(4, 2)) == 0 &&
        MatchesControlEnvelope(packet, localSid, remoteSid) &&
        packet[0x15] == 0x00 &&
        BinaryPrimitives.ReadUInt16BigEndian(
          packet.Slice(0x16, 2)) == innerSequence &&
        BinaryPrimitives.ReadUInt16LittleEndian(
          packet.Slice(0x1A, 2)) == tokenRequest;
    }

    internal static bool IsMatchingAuthReply(
      ReadOnlySpan<byte> packet,
      uint localSid,
      uint remoteSid,
      byte requestType,
      ushort innerSequence,
      ReadOnlySpan<byte> requestAuthId)
    {
      if (requestAuthId.Length != 6)
        return false;

      return
        packet.Length == AuthSize &&
        BinaryPrimitives.ReadUInt32LittleEndian(
          packet.Slice(0, 4)) == AuthSize &&
        BinaryPrimitives.ReadUInt16LittleEndian(
          packet.Slice(4, 2)) == 0 &&
        MatchesControlEnvelope(packet, localSid, remoteSid) &&
        packet[0x14] == 0x02 &&
        packet[0x15] == requestType &&
        BinaryPrimitives.ReadUInt16BigEndian(
          packet.Slice(0x16, 2)) == innerSequence &&
        packet.Slice(0x1A, 2).SequenceEqual(
          requestAuthId.Slice(0, 2));
    }

    internal static bool IsMatchingCapabilitiesResponse(
      ReadOnlySpan<byte> packet,
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      ReadOnlySpan<byte> requestAuthId)
    {
      if (requestAuthId.Length != 6 ||
          packet.Length < CapabilitiesHeaderSize + RadioCapabilitySize ||
          (packet.Length - CapabilitiesHeaderSize) % RadioCapabilitySize != 0)
        return false;

      return
        BinaryPrimitives.ReadUInt32LittleEndian(
          packet.Slice(0, 4)) == packet.Length &&
        BinaryPrimitives.ReadUInt16LittleEndian(
          packet.Slice(4, 2)) == 0 &&
        MatchesControlEnvelope(packet, localSid, remoteSid) &&
        packet[0x14] == 0x02 &&
        packet[0x15] == 0x02 &&
        BinaryPrimitives.ReadUInt16BigEndian(
          packet.Slice(0x16, 2)) == innerSequence &&
        packet.Slice(0x1A, 6).SequenceEqual(requestAuthId);
    }

    internal static bool IsMatchingStreamStatus(
      ReadOnlySpan<byte> packet,
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      ReadOnlySpan<byte> requestAuthId) =>
      IsMatchingStreamResponse(
        packet,
        StatusSize,
        localSid,
        remoteSid,
        innerSequence,
        requestAuthId);

    internal static bool IsMatchingStreamGrant(
      ReadOnlySpan<byte> packet,
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      ReadOnlySpan<byte> requestAuthId) =>
      IsMatchingStreamResponse(
        packet,
        ConnInfoSize,
        localSid,
        remoteSid,
        innerSequence,
        requestAuthId);

    private static bool IsMatchingStreamResponse(
      ReadOnlySpan<byte> packet,
      int expectedLength,
      uint localSid,
      uint remoteSid,
      ushort innerSequence,
      ReadOnlySpan<byte> requestAuthId)
    {
      if (requestAuthId.Length != 6 ||
          packet.Length != expectedLength)
        return false;

      return
        BinaryPrimitives.ReadUInt32LittleEndian(
          packet.Slice(0, 4)) == expectedLength &&
        BinaryPrimitives.ReadUInt16LittleEndian(
          packet.Slice(4, 2)) == 0 &&
        MatchesControlEnvelope(packet, localSid, remoteSid) &&
        packet[0x14] == 0x02 &&
        packet[0x15] == 0x03 &&
        BinaryPrimitives.ReadUInt16BigEndian(
          packet.Slice(0x16, 2)) == innerSequence &&
        packet.Slice(0x1A, 6).SequenceEqual(requestAuthId);
    }

    private static UdpClient CreateBoundClient(
      IPAddress localAddress)
    {
      var client = new UdpClient(
        AddressFamily.InterNetwork);

      client.Client.SetSocketOption(
        SocketOptionLevel.Socket,
        SocketOptionName.ReuseAddress,
        false);

      client.Client.Bind(
        new IPEndPoint(localAddress, 0));

      return client;
    }

    private static IPAddress ResolveLocalAddress(
      IPAddress remoteAddress,
      int remotePort)
    {
      using var socket = new Socket(
        AddressFamily.InterNetwork,
        SocketType.Dgram,
        ProtocolType.Udp);

      socket.Connect(
        new IPEndPoint(
          remoteAddress,
          remotePort));

      return ((IPEndPoint)socket.LocalEndPoint!)
        .Address;
    }

    private static uint BuildSessionId(
      IPAddress localAddress,
      int localPort)
    {
      byte[] bytes = localAddress.GetAddressBytes();

      return
        ((uint)bytes[2] << 24) |
        ((uint)bytes[3] << 16) |
        (uint)(localPort & 0xFFFF);
    }

    private static void WriteSid(
      byte[] packet,
      int offset,
      uint sid) =>
      BinaryPrimitives.WriteUInt32BigEndian(
        packet.AsSpan(offset, 4),
        sid);

    private static string NormalizeClientName(
      string value)
    {
      string text = string.IsNullOrWhiteSpace(value)
        ? "icom-pc"
        : value.Trim();

      byte[] bytes = Encoding.ASCII.GetBytes(text);
      if (bytes.Length <= 15)
        return text;

      return Encoding.ASCII.GetString(
        bytes,
        0,
        15);
    }

    private static void WriteAsciiZ(
      Span<byte> destination,
      string value)
    {
      destination.Clear();
      byte[] bytes = Encoding.ASCII.GetBytes(value);
      bytes.AsSpan(
          0,
          Math.Min(bytes.Length, destination.Length - 1))
        .CopyTo(destination);
    }

    private static string ReadAsciiZ(
      ReadOnlySpan<byte> bytes)
    {
      int zero = bytes.IndexOf((byte)0);
      ReadOnlySpan<byte> text =
        zero >= 0
          ? bytes[..zero]
          : bytes;

      return Encoding.ASCII.GetString(text);
    }

    internal static byte[] EncodePasscode(
      string value)
    {
      // Icom LAN credential substitution table. Index 32 corresponds to ASCII
      // space; entries through index 126 cover printable ASCII.
      byte[] table =
      {
        0x47, 0x5D, 0x4C, 0x42, 0x66, 0x20, 0x23, 0x46,
        0x4E, 0x57, 0x45, 0x3D, 0x67, 0x76, 0x60, 0x41,
        0x62, 0x39, 0x59, 0x2D, 0x68, 0x7E, 0x7C, 0x65,
        0x7D, 0x49, 0x29, 0x72, 0x73, 0x78, 0x21, 0x6E,
        0x5A, 0x5E, 0x4A, 0x3E, 0x71, 0x2C, 0x2A, 0x54,
        0x3C, 0x3A, 0x63, 0x4F, 0x43, 0x75, 0x27, 0x79,
        0x5B, 0x35, 0x70, 0x48, 0x6B, 0x56, 0x6F, 0x34,
        0x32, 0x6C, 0x30, 0x61, 0x6D, 0x7B, 0x2F, 0x4B,
        0x64, 0x38, 0x2B, 0x2E, 0x50, 0x40, 0x3F, 0x55,
        0x33, 0x37, 0x25, 0x77, 0x24, 0x26, 0x74, 0x6A,
        0x28, 0x53, 0x4D, 0x69, 0x22, 0x5C, 0x44, 0x31,
        0x36, 0x58, 0x3B, 0x7A, 0x51, 0x5F, 0x52
      };

      byte[] result = new byte[16];
      byte[] input = Encoding.ASCII.GetBytes(value ?? string.Empty);

      for (int i = 0;
           i < input.Length && i < result.Length;
           i++)
      {
        int p = input[i] + i;

        if (p > 126)
          p = 32 + p % 127;

        if (p >= 32 && p <= 126)
          result[i] = table[p - 32];
      }

      return result;
    }

    private void Publish(string message) =>
      StatusChanged?.Invoke(message);

    private void ThrowIfDisposed()
    {
      if (Disposed)
        throw new ObjectDisposedException(
          nameof(IcomLanDirectSession));
    }

    public void Dispose()
    {
      if (Disposed) return;
      Disposed = true;

      try { Control?.Close(); }
      catch (Exception ex) { Log.Debug("Icom Direct LAN control socket disposal failed ({Type})", ex.GetType().Name); }
      try { Civ?.Close(); }
      catch (Exception ex) { Log.Debug("Icom Direct LAN CI-V socket disposal failed ({Type})", ex.GetType().Name); }
      try { AudioSink?.Close(); }
      catch (Exception ex) { Log.Debug("Icom Direct LAN audio socket disposal failed ({Type})", ex.GetType().Name); }

      Control = null;
      Civ = null;
      AudioSink = null;

      TxAudio.Writer.TryComplete();
      ControlSendLock.Dispose();
      CivSendLock.Dispose();
      AudioSendLock.Dispose();
    }
  }
}
