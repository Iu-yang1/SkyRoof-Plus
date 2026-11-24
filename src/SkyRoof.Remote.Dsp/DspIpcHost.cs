using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using Microsoft.Extensions.DependencyInjection;

namespace SkyRoof.Remote.Dsp;

internal static class DspIpcHost
{
    public static async Task<WebApplication> StartAsync(CwLiveReceiver cw)
    {
        string pipe = Environment.GetEnvironmentVariable("SKYROOF_DSP_PIPE") ?? "skyroof-station-dsp";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [], ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.ConfigureKestrel(server =>
            server.ListenNamedPipe(pipe, options => options.Protocols = HttpProtocols.Http2));
        builder.Services.AddSingleton(cw);
        builder.Services.AddGrpc();
        var app = builder.Build();
        app.MapGrpcService<DspWaveformService>();
        await app.StartAsync();
        return app;
    }
}
