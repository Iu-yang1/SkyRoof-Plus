using System.Diagnostics;

internal static partial class NativeFt4HttpProbe
{
    private const string User = "ft4-test-admin";
    private const string Password = "FixtureFt4Password!2026";

    private static void StartProcesses(NativeFt4ProcessPaths paths, Fixture fixture, List<Process> processes)
    {
        var station = Setup(paths.Station);
        station.Environment["SKYROOF_DEVICE_PIPE"] = fixture.DevicePipe;
        station.Environment["SKYROOF_STATION_PIPE"] = fixture.StationPipe;
        station.Environment["SKYROOF_ORBIT_BACKEND"] = "sgpnet";
        station.Environment["SKYROOF_CATALOG_AUTO"] = "false";
        station.Environment["SKYROOF_ISS_OEM_AUTO"] = "false";
        station.Environment["SKYROOF_TRACKING_JOB_FILE"] = Path.Combine(fixture.Root, "tracking.json");
        var dsp = Setup(paths.Dsp);
        dsp.Environment["SKYROOF_DSP_PIPE"] = fixture.DspPipe;
        dsp.Environment["SKYROOF_RX_AUDIO_BUS"] = "skyroof-ft4-test-rx-" + fixture.DspPipe;
        dsp.Environment["SKYROOF_AF_SPECTRUM_BUS"] = "skyroof-ft4-test-af-" + fixture.DspPipe;
        dsp.Environment["SKYROOF_FT4_RESULTS_BUS"] = "skyroof-ft4-test-results-" + fixture.DspPipe;
        dsp.Environment["SKYROOF_Q65_RESULTS_BUS"] = "skyroof-q65-test-results-" + fixture.DspPipe;
        var host = Setup(paths.Host);
        host.Environment["SKYROOF_STATION_PIPE"] = fixture.StationPipe;
        host.Environment["SKYROOF_DSP_PIPE"] = fixture.DspPipe;
        host.Environment["SKYROOF_Q65_RESULTS_BUS"] = "skyroof-q65-test-results-" + fixture.DspPipe;
        host.Environment["SKYROOF_FT4_RESULTS_BUS"] = "skyroof-ft4-test-results-" + fixture.DspPipe;
        host.Environment["ASPNETCORE_URLS"] = fixture.Address.ToString().TrimEnd('/');
        host.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        host.Environment["SKYROOF_AUTH_DB"] = Path.Combine(fixture.Root, "auth.db");
        host.Environment["SKYROOF_BOOTSTRAP_ADMIN_USER"] = User;
        host.Environment["SKYROOF_BOOTSTRAP_ADMIN_PASSWORD"] = Password;
        foreach (var info in new[] { station, dsp, host })
            processes.Add(Process.Start(info) ?? throw new InvalidOperationException("FT4 E2E child start failed."));
    }

    private static ProcessStartInfo Setup(string dll)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        info.ArgumentList.Add(dll);
        return info;
    }
}
