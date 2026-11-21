using SkyRoof.Remote.Supervisor;

var builder = Host.CreateApplicationBuilder(args);
var path = Environment.GetEnvironmentVariable("SKYROOF_SUPERVISOR_CONFIG");
if (!string.IsNullOrWhiteSpace(path))
    builder.Configuration.AddJsonFile(Path.GetFullPath(path), optional: false, reloadOnChange: false);
builder.Services.AddWindowsService(options => options.ServiceName = "SkyRoof Station Runtime");
builder.Services.AddHostedService<ManagedProcessSupervisor>();
await builder.Build().RunAsync();
