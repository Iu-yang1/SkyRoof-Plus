using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkyRoof.Remote.Host.Identity;
using SkyRoof.Remote.Host.Realtime;

var builder = WebApplication.CreateBuilder(args);
var database = Environment.GetEnvironmentVariable("SKYROOF_AUTH_DB")
    ?? Path.Combine(AppContext.BaseDirectory, "station-users.db");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(database))!);

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
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization();
builder.Services.AddSignalR().AddMessagePackProtocol();

var app = builder.Build();
await StationIdentityBootstrap.InitializeAsync(app.Services);
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));
app.MapGet("/api/v1/station", (System.Security.Claims.ClaimsPrincipal principal) =>
    Results.Ok(new { protocol = "skyroof.remote.v1",
        runtimeMode = "foundation-prototype", user = principal.Identity?.Name }))
    .RequireAuthorization();
app.MapStationAuth();
app.MapHub<StationControlHub>("/hub/control");
if (app.Environment.IsDevelopment()) app.MapSimulatedBinaryStreams();
await app.RunAsync();
