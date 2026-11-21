using System.Diagnostics;
using System.Text.Json;

if (args is ["child", var marker])
{
    File.AppendAllText(marker, DateTime.UtcNow.Ticks + Environment.NewLine);
    if (File.ReadAllLines(marker).Length == 1) Environment.Exit(17);
    await Task.Delay(TimeSpan.FromSeconds(30));
    return;
}

var supervisorDll = Path.GetFullPath(args.Single());
var selfDll = System.Reflection.Assembly.GetExecutingAssembly().Location;
var dir = Path.Combine(Path.GetTempPath(), "skyroof-supervisor-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var config = Path.Combine(dir, "supervisor.json");
var markerPath = Path.Combine(dir, "started.txt");
var data = new
{
    Processes = new[]
    {
        new {
            Name = "simulated-device",
            Executable = "dotnet",
            Arguments = new[] { selfDll, "child", markerPath },
            WorkingDirectory = dir
        }
    }
};
File.WriteAllText(config, JsonSerializer.Serialize(data));
var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
start.ArgumentList.Add(supervisorDll);
start.Environment["SKYROOF_SUPERVISOR_CONFIG"] = config;
using var supervisor = Process.Start(start)
    ?? throw new InvalidOperationException("Supervisor did not start.");
try
{
    var restarted = false;
    for (var i = 0; i < 100; i++)
    {
        if (File.Exists(markerPath) && File.ReadAllLines(markerPath).Length >= 2)
        {
            restarted = true;
            break;
        }
        await Task.Delay(200);
    }
    if (!restarted) throw new InvalidOperationException("Supervisor did not restart crashed child.");
    Console.WriteLine("PASS: independently supervised child process restarted after injected crash.");
}
finally
{
    if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
    await supervisor.WaitForExitAsync();
}
