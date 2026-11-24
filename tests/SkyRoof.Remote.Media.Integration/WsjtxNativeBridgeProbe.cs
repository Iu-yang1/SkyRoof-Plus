using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SkyRoof.Remote.Ipc;
using SkyRoof.Remote.WsjtxBridge;

internal static partial class WsjtxNativeBridgeProbe
{
    internal static async Task RunAsync(HttpClient admin, Uri address,
        FakeNativeIcomRadio radio, CancellationToken ct)
    {
        using var reply = await admin.PostAsJsonAsync("/api/v1/auth/tokens", new { name = "wsjtx-fixture" }, ct);
        reply.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync(ct));
        string token = doc.RootElement.GetProperty("token").GetString() ??
            throw new InvalidDataException("Missing bridge token.");
        await using var hub = new HubConnectionBuilder().WithUrl(new Uri(address, "hub/control"),
            o => o.AccessTokenProvider = () => Task.FromResult<string?>(token)).Build();
        await hub.StartAsync(ct);
        var rig = new RemoteRig(hub);
        await using var ptt = new DigitalPttSession(rig, true);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var cat = new RigctlListener(rig, ptt, 0);
        var running = cat.RunAsync(lifetime.Token);
        try
        {
            await using (var link = await CatLink.ConnectAsync(await cat.Ready.WaitAsync(ct), ct))
                await ExerciseAsync(link, rig, ptt, radio, ct);
            await WaitRxAsync(rig, ct);
            Console.WriteLine("PASS: actual localhost NET rigctl -> authenticated Host -> Station -> native Device CAT, PCM and TCP disconnect RX.");
        }
        finally
        {
            lifetime.Cancel();
            try { await running; }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task ExerciseAsync(CatLink link, IRemoteRig rig, DigitalPttSession ptt,
        FakeNativeIcomRadio radio, CancellationToken ct)
    {
        await link.ExpectAsync("\\chk_vfo", "0", ct);
        await link.VerifyHandshakeAsync(ct);
        await link.ExpectAsync("F 145900000", "RPRT 0", ct);
        await link.ExpectAsync("f", "145900000", ct);
        await link.ExpectAsync("I 435550000", "RPRT 0", ct);
        await link.ExpectAsync("i", "435550000", ct);
        await link.ExpectAsync("M PKTUSB 0", "RPRT 0", ct);
        await link.ExpectAsync("X PKTUSB 0", "RPRT 0", ct);
        await link.ExpectAsync("S 1 VFOB", "RPRT 0", ct);
        await link.ExpectAsync("T 3", "RPRT 0", ct);
        var grant = ptt.Grant ?? throw new InvalidDataException("Native digital owner not granted.");
        var renewed = await rig.ExecuteAsync(new RigCommand(grant.TransactionId,
            DeviceOperation.DeviceDigitalHeartbeat), ct);
        if (renewed.Outcome != "digital-ptt-lease-renewed") throw new InvalidDataException("Digital lease not renewed.");
        var pcm = new byte[1920];
        for (int offset = 0; offset < pcm.Length; offset += 2)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset), 19765);
        for (ulong seq = 0; seq < 5; seq++)
        {
            if (!(await rig.SendPcmAsync(grant, seq, pcm, ct)).Accepted)
                throw new InvalidDataException("Authenticated digital PCM rejected.");
            await Task.Delay(20, ct);
        }
        await radio.WaitForTxSampleAsync(19765, ct);
        await link.ExpectAsync("T 0", "RPRT 0", ct);
        await WaitRxAsync(rig, ct);
        await link.ExpectAsync("T 1", "RPRT 0", ct);
    }

    private static async Task WaitRxAsync(IRemoteRig rig, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        while ((await rig.ReadAsync(deadline.Token)).ActualPtt)
            await Task.Delay(50, deadline.Token);
    }
}
