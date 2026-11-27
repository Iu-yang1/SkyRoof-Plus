using SkyRoof.Remote.Foundation.Workspace;

internal static class WorkspaceTests
{
    public static void Run()
    {
        var router = new WorkspaceRouter();
        router.AddStation("A", "Satellite");
        router.AddStation("B", "EME");
        router.AddStation("C", "SDR");
        Check(router.ActiveStationId == "A");
        Check(router.Stations.Single(x => x.AudioEnabled).Id == "A");

        var pressed = router.KeyDown("PTT", "manual-transaction-A");
        Check(pressed.StationId == "A");
        router.SetActive("B");
        Check(router.Stations.Single(x => x.AudioEnabled).Id == "B");
        Check(router.KeyDown("PTT", "should-not-repeat") == pressed);
        Check(router.KeyUp("PTT") == pressed);
        Check(router.KeyUp("PTT") is null);

        router.SetAudioEnabled("C", true);
        Check(router.Stations.Count(x => x.AudioEnabled) == 2);
        var next = router.KeyDown("PTT", "manual-transaction-B");
        Check(next.StationId == "B");
        router.RemoveStation("B");
        Check(router.KeyUp("PTT") == next);
        Check(router.ActiveStationId == "A");
        Console.WriteLine("PASS: multi-station audio isolation and original PTT shortcut routing.");
    }

    private static void Check(bool valid)
    {
        if (!valid) throw new InvalidOperationException("Workspace simulation failed.");
    }
}
