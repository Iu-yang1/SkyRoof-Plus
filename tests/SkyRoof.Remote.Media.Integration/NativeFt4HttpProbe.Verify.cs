using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json;
using Grpc.Net.Client;
using SkyRoof.Remote.Ipc;

internal static partial class NativeFt4HttpProbe
{
    private static async Task VerifyHttpAsync(HttpClient anonymous, Uri address,
        string dspPipe, FakeNativeIcomRadio fake, bool q65Runtime, CancellationToken ct)
    {
        var request = new { message = "CQ K1ABC FN42", frequencyHz = 435_550_000L,
            audioHz = 1500, gain = 0.5f, odd = false };
        using var denied = await anonymous.PostAsJsonAsync("/api/v1/dsp/ft4/transmit", request, ct);
        if (denied.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Anonymous timed FT4 TX was not denied.");
        using var cookies = new HttpClientHandler { CookieContainer = new CookieContainer() };
        using var admin = new HttpClient(cookies) { BaseAddress = address };
        using var login = await admin.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = User, password = Password }, ct);
        login.EnsureSuccessStatusCode();
        byte[] expected = await ReadWaveformAsync(dspPipe, ct);
        fake.BeginWaveformCapture();
        using var response = await admin.PostAsJsonAsync("/api/v1/dsp/ft4/transmit", request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("FT4 transmit: " + body);
        using var receipt = JsonDocument.Parse(body);
        if (receipt.RootElement.GetProperty("state").GetString() != "completed")
            throw new InvalidOperationException("FT4 did not complete.");
        short[] captured = fake.EndWaveformCapture();
        if (captured.Length * 2 != expected.Length ||
            captured.Where((sample, i) => sample != BinaryPrimitives.ReadInt16LittleEndian(
                expected.AsSpan(i * 2, 2))).Any())
            throw new InvalidDataException("Authenticated FT4 TX waveform differs from native DSP encoding.");
        Console.WriteLine("PASS: authenticated Host -> native DSP FT4 encoding -> Station -> Device -> exact UDP waveform.");
        await WsjtxNativeBridgeProbe.RunAsync(admin, address, fake, ct);
        await VerifyAutoAsync(anonymous, admin, dspPipe, fake, ct);
        if (q65Runtime) await VerifyQ65Async(anonymous, admin, dspPipe, fake, ct);
        else Console.WriteLine("Q65 UDP fixture not applicable: standalone FT4/CW build without native Q65 runtime.");
    }

    private static async Task<byte[]> ReadWaveformAsync(string pipe, CancellationToken ct, string message = "CQ K1ABC FN42")
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.None);
                try { await stream.ConnectAsync(5000, token); return stream; }
                catch { await stream.DisposeAsync(); throw; }
            }
        };
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        var waveform = await new DspWaveform.DspWaveformClient(channel).EncodeFt4Async(new Ft4EncodeRequest
        {
            Message = message, AudioHz = 1500, Gain = 0.5f
        }, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: ct);
        if (waveform.Pcm16.Length != 241920 * 2 || waveform.SampleRate != 48000)
            throw new InvalidDataException("Incorrect FT4 waveform IPC geometry.");
        return waveform.Pcm16.ToByteArray();
    }
}
