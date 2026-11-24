using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkyRoof.Remote.Host.Identity;
using SkyRoof.Remote.Host.Realtime;

var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var webUiAvailable = File.Exists(Path.Combine(webRoot, "index.html"));
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = webUiAvailable ? webRoot : null
});
var database = Environment.GetEnvironmentVariable("SKYROOF_AUTH_DB")
    ?? Path.Combine(AppContext.BaseDirectory, "station-users.db");
var databaseFolder = Path.GetDirectoryName(Path.GetFullPath(database))
    ?? throw new InvalidOperationException("Invalid station database path.");
Directory.CreateDirectory(databaseFolder);

builder.Services.AddDbContext<StationIdentityDb>(options =>
    options.UseSqlite("Data Source=" + database));
builder.Services.AddIdentity<StationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<StationIdentityDb>().AddDefaultTokenProviders();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "SkyRoofAuth";
    options.DefaultChallengeScheme = "SkyRoofAuth";
}).AddPolicyScheme("SkyRoofAuth", null, options =>
{
    options.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.Authorization.ToString().StartsWith(
            "Bearer ", StringComparison.OrdinalIgnoreCase)
            ? "SkyRoofToken" : IdentityConstants.ApplicationScheme;
}).AddScheme<AuthenticationSchemeOptions, StationTokenHandler>("SkyRoofToken", null);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.Name = "skyroof.station";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization();
// SignalR defaults to one active invocation per client: a delayed rotator
// readback would queue an urgent STOP behind POINT on the same operator socket.
// Device serializes hardware commands and owns the separate STOP TCP session.
builder.Services.AddSignalR(options =>
{
    options.MaximumParallelInvocationsPerClient = 8;
}).AddMessagePackProtocol();
builder.Services.AddSingleton<StationDeviceBridge>();
builder.Services.AddSingleton<MediaStatusBridge>();

var app = builder.Build();
await StationIdentityBootstrap.InitializeAsync(app.Services);
if (webUiAvailable)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (StationDeviceBridge station, CancellationToken cancellationToken) =>
{
    try
    {
        await station.ReadbackAsync(cancellationToken);
        return Results.Ok(new { status = "ready", dependencies = "station-device" });
    }
    catch (Exception ex) when (ex is Microsoft.AspNetCore.SignalR.HubException
        or IOException or OperationCanceledException)
    {
        return Results.Problem(title: "Station or Device not ready",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapGet("/api/v1/station", (System.Security.Claims.ClaimsPrincipal principal) =>
    Results.Ok(new { protocol = "skyroof.remote.v1",
        runtimeMode = "foundation-prototype", user = principal.Identity?.Name }))
    .RequireAuthorization();
app.MapStationAuth();
app.MapStationAdministration();
app.MapLiveAudioStream();
app.MapLiveRtcRx();
app.MapMediaStatus();
app.MapLiveAfSpectrum();
app.MapLiveRfScope();
app.MapFt4Results();
app.MapHub<StationControlHub>("/hub/control");
if (app.Environment.IsDevelopment()) app.MapSimulatedBinaryStreams();
if (webUiAvailable) app.MapFallbackToFile("index.html");
await app.RunAsync();
