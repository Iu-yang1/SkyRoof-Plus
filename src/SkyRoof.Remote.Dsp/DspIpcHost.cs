using SkyRoof.Remote.Ipc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using Microsoft.Extensions.DependencyInjection;

namespace SkyRoof.Remote.Dsp;

internal static class DspIpcHost
{
    public static async Task<WebApplication> StartAsync(CwLiveReceiver cw, Q65LiveReceiver q65, TelemetryLiveReceiver telemetry)
    {
        string pipe = Environment.GetEnvironmentVariable("SKYROOF_DSP_PIPE") ?? "skyroof-station-dsp";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [], ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseNamedPipes(options =>
        {
            var security = WindowsPipeAccessPolicy.Create("SKYROOF_DSP_PIPE_CLIENT_SIDS");
            options.CurrentUserOnly = security is null;
            if (security is not null) options.PipeSecurity = security;
        });
        builder.WebHost.ConfigureKestrel(server =>
            server.ListenNamedPipe(pipe, options => options.Protocols = HttpProtocols.Http2));
        builder.Services.AddSingleton(cw);
        builder.Services.AddSingleton(q65);
        builder.Services.AddSingleton(telemetry);
        builder.Services.AddGrpc(options => options.MaxReceiveMessageSize = 32 * 1024 * 1024);
        var app = builder.Build();
        app.MapGrpcService<DspWaveformService>();
        await app.StartAsync();
        return app;
    }
}
