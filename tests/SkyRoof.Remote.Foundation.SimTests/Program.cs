using SkyRoof.Remote.Foundation.Media;
using SkyRoof.Remote.Foundation.Signals;
using SkyRoof.Remote.Foundation.Timing;
using SkyRoof.Remote.Foundation.Transmission;

var radio = new RadioTxConfiguration("ic9700", "USB", "LAN", 435_650_000);
var count = 0;
void Test(string title, Action body)
{
    body();
    count++;
    Console.WriteLine("[PASS] " + title);
}
void Check(bool value)
{
    if (!value) throw new InvalidOperationException("Simulation failed.");
}
void Reject(Action work)
{
    try { work(); } catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Expected invalid operation.");
}

Test("PTT initial assertion, takeover, stale release and final RX", () =>
{
    var tx = new TransmitCoordinator();
    Check(tx.BeginManual("a", "operator-a", radio).Action == RfAction.Assert);
    var b = tx.BeginManual("b", "operator-b", radio);
    Check(b.Action == RfAction.Keep && b.ClearPreviousAudio && b.Current?.Epoch == 2);
    Check(!tx.ReleaseManual("a", "operator-a").Changed);
    Check(tx.Current?.OwnerSessionId == "operator-b");
    Check(tx.ReleaseManual("b", "operator-b").Action == RfAction.Release);
    Check(tx.Current is null);
});
Test("Changing radio mode requires hardware reconfiguration", () =>
{
    var tx = new TransmitCoordinator();
    tx.BeginManual("a", "a", radio);
    Check(tx.BeginManual("b", "b", radio with { Mode = "FM" }).Action == RfAction.Reconfigure);
});
Test("Repeated manual request is idempotent", () =>
{
    var tx = new TransmitCoordinator();
    tx.BeginManual("a", "a", radio);
    Check(!tx.BeginManual("a", "a", radio).Changed && tx.Current?.Epoch == 1);
});
Test("Digital TX preempted, future automation paused but RX independent", () =>
{
    var tx = new TransmitCoordinator();
    Check(tx.BeginDigital("slot1", radio).Changed);
    var takeover = tx.BeginManual("a", "operator-a", radio);
    Check(takeover.InterruptedDigital && takeover.DigitalAutomationPaused);
    Check(!tx.CompleteDigital("slot1").Changed);
    tx.ReleaseManual("a", "operator-a");
    Check(tx.Current is null && tx.DigitalAutomationPaused);
    Check(!tx.BeginDigital("slot2", radio).Changed);
    tx.EnableDigitalAutomation();
    Check(tx.BeginDigital("slot2", radio).Changed);
});
Test("Optional automation resume never replays interrupted slot", () =>
{
    var tx = new TransmitCoordinator(resumeDigitalAfterManual: true);
    tx.BeginDigital("slot1", radio);
    tx.BeginManual("a", "a", radio);
    tx.ReleaseManual("a", "a");
    Check(tx.Current is null && !tx.DigitalAutomationPaused);
});
Test("Stale disconnect cannot release another operator", () =>
{
    var tx = new TransmitCoordinator();
    tx.BeginManual("a", "a", radio);
    tx.BeginManual("b", "b", radio);
    Check(!tx.Disconnect("a").Changed);
    Check(tx.Disconnect("b").Action == RfAction.Release);
});
Test("STOP and restart do not restore manual TX", () =>
{
    var tx = new TransmitCoordinator();
    tx.BeginManual("a", "a", radio);
    Check(tx.StopAll().Action == RfAction.Release);
    tx.BeginManual("b", "b", radio);
    Check(tx.RecoverAfterRestart().Action == RfAction.Release);
    Check(tx.Current is null);
});
Test("Parallel operator actions serialize and preserve latest transaction", () =>
{
    var tx = new TransmitCoordinator();
    Parallel.For(0, 128, i => tx.BeginManual("tx" + i, "session" + i, radio));
    var owner = tx.Current ?? throw new InvalidOperationException();
    Parallel.For(0, 128, i => {
        if ("tx" + i != owner.TransactionId) tx.ReleaseManual("tx" + i, "session" + i);
    });
    Check(tx.Current?.TransactionId == owner.TransactionId);
    Check(tx.ReleaseManual(owner.TransactionId, owner.OwnerSessionId).Action == RfAction.Release);
});
Test("Opus uses Audio Track; PCM uses DataChannel", () =>
{
    var caps = new MediaCapabilities(true, true, true, true);
    Check(TransportSelector.ChooseInitial(MediaCodec.Opus, caps) == MediaTransport.AudioTrack);
    Check(TransportSelector.ChooseInitial(MediaCodec.Pcm16, caps) == MediaTransport.DataChannel);
    Check(TransportSelector.ChooseInitial(MediaCodec.Float32, caps) == MediaTransport.DataChannel);
});
Test("ICE failure returns WSS, media-specific failure returns RTC Data", () =>
{
    var caps = new MediaCapabilities(true, true, true, true);
    Check(TransportSelector.ChooseFallback(MediaCodec.Opus, MediaTransport.AudioTrack,
        MediaFailure.IceUnavailable, caps) == MediaTransport.WebSocket);
    Check(TransportSelector.ChooseFallback(MediaCodec.Opus, MediaTransport.AudioTrack,
        MediaFailure.TrackNegotiation, caps) == MediaTransport.DataChannel);
    Check(TransportSelector.ChooseFallback(MediaCodec.Pcm16, MediaTransport.DataChannel,
        MediaFailure.IceUnavailable, caps) == MediaTransport.WebSocket);
    Check(TransportSelector.ChooseFallback(MediaCodec.Pcm16, MediaTransport.WebSocket,
        MediaFailure.WebSocketCongested, caps) == MediaTransport.DataChannel);
    Check(TransportSelector.ChooseInitial(MediaCodec.Opus,
        new MediaCapabilities(false, false, false, false)) is null);
});
Test("SAP/1 audio binary header round-trip and size", () =>
{
    var original = new AudioFrameHeader(2, 7, 9, 12, 1001, 48000, 123456789,
        48000, 480, 1, 960);
    var bytes = new byte[AudioFrameHeader.Size + 960];
    original.Write(bytes);
    Check(bytes[0] == (byte)'S' && bytes[1] == (byte)'A'
        && bytes[2] == (byte)'P' && bytes[3] == (byte)'1');
    Check(AudioFrameHeader.Size == 52);
    Check(AudioFrameHeader.TryRead(bytes, out var parsed) && parsed == original);
});
Test("SAP/1 rejects truncation and unknown version", () =>
{
    var header = new AudioFrameHeader(1, 0, 1, 1, 1, 0, 10, 48000, 480, 1, 10);
    var bytes = new byte[AudioFrameHeader.Size + 10];
    header.Write(bytes);
    Check(!AudioFrameHeader.TryRead(bytes.AsSpan(0, 40), out _));
    bytes[4] = 2;
    Check(!AudioFrameHeader.TryRead(bytes, out _));
    bytes[4] = 1;
    Check(!AudioFrameHeader.TryRead(bytes.AsSpan(0, bytes.Length - 1), out _));
});
Test("Ten thousand sequential audio blocks preserve sample continuity", () =>
{
    var packet = new byte[AudioFrameHeader.Size];
    for (ulong i = 0; i < 10000; i++)
    {
        var h = new AudioFrameHeader(1, 0, 2, 10, i, i * 480, (long)i * 10_000_000,
            48000, 480, 1, 0);
        h.Write(packet);
        Check(AudioFrameHeader.TryRead(packet, out var parsed));
        Check(parsed.Sequence == i && parsed.FirstSampleIndex == i * 480);
    }
});
Test("Timestamped job arm and monotonic start time", () =>
{
    var scheduler = new TimedTxScheduler();
    var id = Guid.NewGuid();
    scheduler.Seal(id, "operator", 10_100, 48000, 48000, new string('a', 64));
    var clock = new StationClockAnchor(10_000, 500);
    var armed = scheduler.Arm(id, clock, 510, 50);
    Check(armed.ArmedMonotonicNanoseconds == 600);
    Reject(() => scheduler.Start(id, 599));
    Check(scheduler.Start(id, 600).Status == TimedTxStatus.Started);
    Check(scheduler.Complete(id).Status == TimedTxStatus.Completed);
});
Test("Timestamped duplicate ID with different content rejected", () =>
{
    var scheduler = new TimedTxScheduler();
    var id = Guid.NewGuid();
    var hash = new string('b', 64);
    var first = scheduler.Seal(id, "operator", 10_100, 12_000, 120, hash);
    Check(scheduler.Seal(id, "operator", 10_100, 12_000, 120, hash) == first);
    Reject(() => scheduler.Seal(id, "operator", 10_101, 12_000, 120, hash));
    Reject(() => scheduler.Arm(id, new StationClockAnchor(10_000, 500), 590, 20));
});
Test("Restart retains future jobs but never replays expired or started jobs", () =>
{
    var scheduler = new TimedTxScheduler();
    var clock = new StationClockAnchor(10_000, 500);
    var future = Guid.NewGuid();
    var expired = Guid.NewGuid();
    var started = Guid.NewGuid();
    scheduler.Seal(future, "operator", 10_200, 48000, 480, new string('c', 64));
    scheduler.Seal(expired, "operator", 10_050, 48000, 480, new string('d', 64));
    scheduler.Seal(started, "operator", 10_100, 48000, 480, new string('e', 64));
    scheduler.Arm(future, clock, 510, 5);
    scheduler.Arm(started, clock, 510, 5);
    scheduler.Start(started, 600);
    var jobs = scheduler.Recover(clock, 620);
    Check(jobs.Count == 1 && jobs[0].Id == future);
    Check(scheduler.Get(expired).Status == TimedTxStatus.Expired);
    Check(scheduler.Get(started).Status == TimedTxStatus.Cancelled);
    Check(scheduler.Get(future).Status == TimedTxStatus.Sealed);
});
Console.WriteLine($"PASS: {count} deterministic simulation groups.");
