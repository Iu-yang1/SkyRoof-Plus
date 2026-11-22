using SkyRoof.Remote.Supervisor;

var builder = Host.CreateApplicationBuilder(args);
var path = builder.Configuration["config"] ??
    Environment.GetEnvironmentVariable("SKYROOF_SUPERVISOR_CONFIG");
if (!string.IsNullOrWhiteSpace(path))
    builder.Configuration.AddJsonFile(Path.GetFullPath(path), optional: false, reloadOnChange: false);
var serviceName = builder.Configuration["service-name"] ?? "SkyRoofRemote";
builder.Services.AddWindowsService(options => options.ServiceName = serviceName);
builder.Services.AddHostedService<ManagedProcessSupervisor>();
await builder.Build().RunAsync();
