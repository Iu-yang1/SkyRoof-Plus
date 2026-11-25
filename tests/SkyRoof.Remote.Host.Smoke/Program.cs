using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.SignalBus;
using System.Diagnostics;

var reserve = new TcpListener(IPAddress.Loopback, 0);
reserve.Start();
var port = ((IPEndPoint)reserve.LocalEndpoint).Port;
reserve.Stop();
var url = $"http://127.0.0.1:{port}";
var database = Path.Combine(Path.GetTempPath(), $"skyroof-smoke-{Guid.NewGuid():N}.db");
var busName = "rx-smoke-" + Guid.NewGuid().ToString("N");
using var bus = new NamedSignalRing(busName, slots: 64, payloadCapacity: 4096, initialEpoch: 33);
var afBusName = "af-smoke-" + Guid.NewGuid().ToString("N");
using var afBus = new NamedSignalRing(afBusName, 64, 4096, initialEpoch: 44);
var digital = new DspResultHttpProbe();
using var digitalLifetime = digital;
await using var camera = await CameraHttpFixture.StartAsync();
var processInfo = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false };
processInfo.ArgumentList.Add(Path.GetFullPath(args.Single()));
processInfo.Environment["ASPNETCORE_URLS"] = url;
processInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
processInfo.Environment["SKYROOF_AUTH_DB"] = database;
processInfo.Environment["SKYROOF_RX_AUDIO_BUS"] = busName;
processInfo.Environment["SKYROOF_AF_SPECTRUM_BUS"] = afBusName;
digital.Configure(processInfo);
camera.Configure(processInfo);
processInfo.Environment["SKYROOF_BOOTSTRAP_ADMIN_USER"] = "station-admin";
processInfo.Environment["SKYROOF_BOOTSTRAP_ADMIN_PASSWORD"] = "ExampleStationAdminPassw0rd!";
using var host = System.Diagnostics.Process.Start(processInfo)
    ?? throw new InvalidOperationException("Could not launch remote host.");

try
{
    using var anon = new HttpClient { BaseAddress = new Uri(url) };
    var available = false;
    for (var attempt = 0; attempt < 80; attempt++)
    {
        try
        {
            if ((await anon.GetAsync("/health/live")).IsSuccessStatusCode)
            {
                available = true;
                break;
            }
        }
        catch (HttpRequestException) { }
        await Task.Delay(150);
    }
    Ensure(available, "Remote Host startup failed.");
    Ensure((await anon.GetAsync("/health/ready")).StatusCode == HttpStatusCode.ServiceUnavailable,
        "Host claimed station readiness despite missing Device IPC.");
    Ensure((await anon.GetAsync("/api/v1/station")).StatusCode == HttpStatusCode.Unauthorized,
        "Anonymous station access not rejected.");
    Ensure((await anon.GetAsync("/api/v1/media/rx-status")).StatusCode ==
        HttpStatusCode.Unauthorized,
        "Anonymous Media diagnostics were exposed.");
    using var jar = new HttpClientHandler { CookieContainer = new CookieContainer() };
    using var user = new HttpClient(jar) { BaseAddress = new Uri(url) };
    var login = await user.PostAsJsonAsync("/api/v1/auth/login",
        new { userName = "station-admin", password = "ExampleStationAdminPassw0rd!" });
    Ensure(login.IsSuccessStatusCode, "Password Cookie login failed.");
    Ensure((await user.GetAsync("/api/v1/station")).IsSuccessStatusCode, "Cookie authorization failed.");
    Ensure((await user.GetAsync("/api/v1/media/rx-status")).StatusCode ==
        HttpStatusCode.ServiceUnavailable,
        "Host hid missing Media IPC instead of reporting it as 503.");
    await digital.VerifyAsync(anon, user);
    await CameraHttpFixture.VerifyAsync(anon, user);
    var created = await user.PostAsJsonAsync("/api/v1/auth/tokens", new { name = "CI test" });
    created.EnsureSuccessStatusCode();
    using var tokenJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
    var token = tokenJson.RootElement.GetProperty("token").GetString()!;
    var tokenId = tokenJson.RootElement.GetProperty("id").GetGuid();
    using var bearer = new HttpClient { BaseAddress = new Uri(url) };
    bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    Ensure((await bearer.GetAsync("/api/v1/station")).IsSuccessStatusCode, "Bearer auth failed.");

    await using (var hub = new HubConnectionBuilder().WithUrl(url + "/hub/control", opts =>
        opts.AccessTokenProvider = () => Task.FromResult<string?>(token))
        .AddMessagePackProtocol().Build())
    {
        await hub.StartAsync();
        Ensure(await hub.InvokeAsync<string>("PingCommandPlane", "probe-id") == "probe-id",
            "MessagePack SignalR request failed.");
        await hub.StopAsync();
    }
    using (var ws = new ClientWebSocket())
    {
        ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/sim/ws/audio"), CancellationToken.None);
        var audio = new AudioFrameHeader
        {
            Codec = 2, Flags = 0, StreamId = 1, StreamEpoch = 1, Sequence = 4,
            FirstSampleIndex = 480, UtcNanoseconds = 0, SampleRate = 48000,
            SamplesPerChannel = 4, Channels = 1, PayloadLength = 8
        };
        var packet = new byte[AudioFrameHeader.Size + 8];
        audio.Write(packet);
        await ws.SendAsync(packet.AsMemory(), WebSocketMessageType.Binary, true, CancellationToken.None);
        var reply = await ws.ReceiveAsync(new byte[1024].AsMemory(), CancellationToken.None);
        Ensure(reply.MessageType == WebSocketMessageType.Binary && reply.Count == packet.Length,
            "Binary SAP/1 WSS echo failed.");
    }

    Ensure((await anon.GetAsync("/ws/audio")).StatusCode == HttpStatusCode.Unauthorized,
        "Anonymous access to real WSS audio feed allowed");
    using (var media = new ClientWebSocket())
    {
        media.Options.SetRequestHeader("Authorization", "Bearer " + token);
        await media.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/audio"), CancellationToken.None);
        var pcm = Enumerable.Range(0, 960).Select(i => (byte)(i % 251)).ToArray();
        bus.Publish(4800, Stopwatch.GetTimestamp(), 48000, 480, pcm);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var packet = new byte[AudioFrameHeader.Size + pcm.Length];
        var response = await media.ReceiveAsync(packet.AsMemory(), deadline.Token);
        Ensure(response.MessageType == WebSocketMessageType.Binary &&
            response.Count == packet.Length &&
            AudioFrameHeader.TryRead(packet, out var parsed) &&
            parsed.Codec == 2 &&
            parsed.StreamEpoch == 33 &&
            parsed.FirstSampleIndex == 4800 &&
            parsed.SampleRate == 48000 &&
            parsed.SamplesPerChannel == 480 &&
            parsed.PayloadLength == pcm.Length &&
            packet.AsSpan(AudioFrameHeader.Size).SequenceEqual(pcm),
            "Real cross-process PCM16 SignalBus -> authenticated WSS SAP/1 failed");
    }
    Console.WriteLine("PASS: live cross-process raw PCM16 WSS over authenticated Station Host.");

    Ensure((await anon.GetAsync("/ws/af-spectrum")).StatusCode == HttpStatusCode.Unauthorized,
        "Anonymous AF spectrum access allowed");
    using (var afSocket = new ClientWebSocket())
    {
        afSocket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        await afSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/af-spectrum"),
            CancellationToken.None);
        var bins = new byte[475 * sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(bins.AsSpan(20 * 4), -6.25f);
        afBus.Publish(4096, Stopwatch.GetTimestamp(), 48000, 475, bins);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var wire = new byte[24 + bins.Length];
        var message = await afSocket.ReceiveAsync(wire.AsMemory(), timeout.Token);
        Ensure(message.MessageType == WebSocketMessageType.Binary &&
            message.Count == wire.Length &&
            BinaryPrimitives.ReadUInt32LittleEndian(wire) == 0x31505353 &&
            BinaryPrimitives.ReadInt64LittleEndian(wire.AsSpan(8, 8)) == 0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(20, 4)) == 44 &&
            BinaryPrimitives.ReadSingleLittleEndian(wire.AsSpan(24 + 20 * 4, 4)) == -6.25f,
            "Real DSP AF SignalBus -> authenticated WSS frame was incorrect");
    }
    Console.WriteLine("PASS: authenticated 475-bin AF spectrum WSS preserves epoch and float bins.");

    var codeReply = await user.PostAsJsonAsync("/api/v1/auth/login-codes", new { });
    codeReply.EnsureSuccessStatusCode();
    using var codeJson = JsonDocument.Parse(await codeReply.Content.ReadAsStringAsync());
    var code = codeJson.RootElement.GetProperty("code").GetString()!;
    using var secondJar = new HttpClientHandler { CookieContainer = new CookieContainer() };
    using var second = new HttpClient(secondJar) { BaseAddress = new Uri(url) };
    Ensure((await second.PostAsJsonAsync("/api/v1/auth/redeem", new { code })).IsSuccessStatusCode,
        "One-use login code not redeemed.");
    Ensure((await second.PostAsJsonAsync("/api/v1/auth/redeem", new { code }))
        .StatusCode == HttpStatusCode.Unauthorized, "One-use login code was replayed.");
    Ensure((await second.GetAsync("/api/v1/station")).IsSuccessStatusCode,
        "One-use code cookie not authorized.");
    using var authorizedStream = new ClientWebSocket();
    authorizedStream.Options.SetRequestHeader("Authorization", "Bearer " + token);
    await authorizedStream.ConnectAsync(
        new Uri($"ws://127.0.0.1:{port}/ws/audio"), CancellationToken.None);

    using var spectrumRevocation = new ClientWebSocket();
    spectrumRevocation.Options.SetRequestHeader("Authorization", "Bearer " + token);
    await spectrumRevocation.ConnectAsync(
        new Uri($"ws://127.0.0.1:{port}/ws/af-spectrum"), CancellationToken.None);

    Ensure((await user.DeleteAsync("/api/v1/auth/tokens/" + tokenId)).StatusCode ==
        HttpStatusCode.NoContent, "Token revocation failed.");
    Ensure((await bearer.GetAsync("/api/v1/station")).StatusCode == HttpStatusCode.Unauthorized,
        "Revoked token remained authorized.");
    using var accessDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    // The ring may already contain audio sent before the revoke completed.
    // Consume those queued frames before asserting the policy close.
    var receiveBuffer = new byte[8192];
    ValueWebSocketReceiveResult close;
    do
    {
        close = await authorizedStream.ReceiveAsync(receiveBuffer.AsMemory(), accessDeadline.Token);
    } while (close.MessageType != WebSocketMessageType.Close);
    Ensure(authorizedStream.CloseStatus == WebSocketCloseStatus.PolicyViolation,
        "Active raw PCM WebSocket remained open after token revocation");
    using var afDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    ValueWebSocketReceiveResult afClosed;
    do
    {
        afClosed = await spectrumRevocation.ReceiveAsync(receiveBuffer.AsMemory(), afDeadline.Token);
    } while (afClosed.MessageType != WebSocketMessageType.Close);
    Ensure(spectrumRevocation.CloseStatus == WebSocketCloseStatus.PolicyViolation,
        "Active AF spectrum WSS was not revoked");
    Console.WriteLine("PASS: revoked Access Token terminates ongoing audio WSS.");

    Console.WriteLine("PASS: Identity/SQLite, token/code, SignalR MessagePack, binary WebSocket.");
}
finally
{
    if (!host.HasExited) host.Kill(entireProcessTree: true);
    await host.WaitForExitAsync();
}
static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
