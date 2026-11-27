using System.Diagnostics;

internal static partial class DeviceIcomAudioProbe
{
    private static ProcessStartInfo CreateDeviceProcess(string deviceDll, string mediaPipe,
        string suffix, string journal, string timedDir, FakeNativeIcomRadio fake)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        info.ArgumentList.Add(deviceDll);
        info.Environment["SKYROOF_DEVICE_PIPE"] = "skyroof-native-icom-test-" + suffix;
        info.Environment["SKYROOF_DEVICE_PTT_JOURNAL"] = journal;
        File.WriteAllText(journal, System.Text.Json.JsonSerializer.Serialize(new
        {
            BackendId = $"IcomLanSatellite:127.0.0.1:{fake.ControlPort}",
            TransactionId = "native-cw:orphan-fixture",
            SessionId = "previous-process", StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-5)
        }));
        info.Environment["SKYROOF_CAT_HOST"] = "127.0.0.1";
        info.Environment["SKYROOF_CAT_PORT"] = "1";
        info.Environment["SKYROOF_CAT_DIALECT"] = "icom-lan-satellite";
        info.Environment["SKYROOF_MEDIA_PIPE"] = mediaPipe;
        info.Environment["SKYROOF_ICOM_LAN_HOST"] = "127.0.0.1";
        info.Environment["SKYROOF_ICOM_LAN_PORT"] = fake.ControlPort.ToString();
        info.Environment["SKYROOF_ICOM_LAN_CAT_PROBE"] = "true";
        info.Environment["SKYROOF_ICOM_LAN_TX_RATE"] = "48000";
        info.Environment["SKYROOF_TIMED_TX_DIRECTORY"] = timedDir;
        info.Environment["SKYROOF_ICOM_LAN_USERNAME"] = "operator";
        info.Environment["SKYROOF_ICOM_LAN_PASSWORD"] = "fake-session-credential";
        return info;
    }
}
