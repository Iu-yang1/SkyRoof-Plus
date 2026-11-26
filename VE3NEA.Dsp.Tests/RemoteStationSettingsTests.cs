using System.Text.Json.Nodes;
using SkyRoof.RemoteDesktop;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class RemoteStationSettingsTests
{
    [Fact]
    public void StartupProfilePersistsAndDefaultsToLocalMode()
    {
        string root = Path.Combine(Path.GetTempPath(), "skyroof-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "profile.json");
            Assert.Equal(DesktopStationMode.Standalone, DesktopStationSettings.Load(path).Mode);
            var profile = new DesktopStationSettings { Mode = DesktopStationMode.ServerWorkspace,
                StationId = "beta", StationPipe = "skyroof-beta-station", HostOrigin = "https://station.example" };
            profile.Save(path);
            var restored = DesktopStationSettings.Load(path);
            Assert.Equal(profile.Mode, restored.Mode);
            Assert.Equal(profile.StationPipe, restored.StationPipe);
            Assert.Equal(profile.HostOrigin, restored.HostOrigin);
            Assert.Equal("SkyRoofRemote-beta", restored.ServiceName);
            profile.Mode = DesktopStationMode.Standalone;
            profile.Save(path);
            Assert.Equal(DesktopStationMode.Standalone, DesktopStationSettings.Load(path).Mode);
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("http://station.example")]
    [InlineData("https://user:pass@station.example")]
    [InlineData("https://station.example/foo")]
    public void HostOriginRejectsInsecureOrAmbiguousAddress(string address)
    {
        var profile = new DesktopStationSettings { HostOrigin = address };
        Assert.Throws<InvalidDataException>(profile.Validate);
    }

    [Fact]
    public void AuthorizeDesktopPreservesOtherProcessConfigurationAndExistingSid()
    {
        var document = JsonNode.Parse("""
            {"Processes":[{"Name":"Station","Executable":"dotnet","Environment":{"SKYROOF_STATION_PIPE_CLIENT_SIDS":"S-1-5-21-1"}},
            {"Name":"Device","Environment":{"SKYROOF_ICOM_LAN_HOST":"192.0.2.10"}}]}
            """)?.AsObject() ?? throw new InvalidDataException();
        StationConfigurationFields.AddDesktopSid(document, "S-1-5-21-2");
        StationConfigurationFields.AddDesktopSid(document, "S-1-5-21-2");
        Assert.Equal("S-1-5-21-1;S-1-5-21-2", StationConfigurationFields.ReadEnvironment(document, "Station")["SKYROOF_STATION_PIPE_CLIENT_SIDS"]?.GetValue<string>());
        Assert.Equal("192.0.2.10", StationConfigurationFields.ReadEnvironment(document, "Device")["SKYROOF_ICOM_LAN_HOST"]?.GetValue<string>());
        Assert.Contains("SKYROOF_SDR_DEVICE_ARGS", StationConfigurationFields.ForProcess("Station"));
        Assert.Contains("SKYROOF_BOOTSTRAP_ADMIN_PASSWORD", StationConfigurationFields.ForProcess("Host"));
    }
}
