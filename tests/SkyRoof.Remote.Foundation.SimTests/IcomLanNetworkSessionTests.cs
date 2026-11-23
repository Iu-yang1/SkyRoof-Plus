using SkyRoof.Remote.Foundation.IcomLan;

internal static class IcomLanNetworkSessionTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        using var radio = new FakeIcomLanControlRadio();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task fakeRadio = radio.RunAsync(timeout.Token);
        await using (var session = await IcomLanControlConnection.OpenAsync(
            new IcomLanConnectionOptions("127.0.0.1", radio.Port,
                "operator", "secret", "SkyRoof", 41002, 41003), timeout.Token))
        {
            if (session.LocalSid == 0 || session.RemoteSid != 0xAABBCCDD ||
                session.Capabilities is not { RadioName: "IC-9700", CivAddress: 0xA2 } ||
                session.NegotiatedRxRate != 48000 ||
                session.RemoteCivPort != 50002 || session.RemoteAudioPort != 50003)
                throw new InvalidOperationException("Independent control session failed radio handshake.");
        }
        await fakeRadio;
        Console.WriteLine("PASS: real UDP Direct LAN greeting, login, rotated token, capabilities and media grant.");
    }
}
