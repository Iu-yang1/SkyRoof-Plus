namespace SkyRoof.Remote.Host.Identity;

internal static class StationWorkspaceFramePolicy
{
    internal static void UseStationWorkspaceFramePolicy(this WebApplication app)
    {
        var configured = Environment.GetEnvironmentVariable("SKYROOF_WORKSPACE_ORIGINS") ?? "";
        var origins = configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseOrigin).Distinct(StringComparer.Ordinal).ToArray();
        var policy = "frame-ancestors 'self'" + (origins.Length == 0 ? "" : " " + string.Join(" ", origins));
        app.Use(async (context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy = policy;
            await next(context);
        });
    }

    private static string ParseOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("SKYROOF_WORKSPACE_ORIGINS requires semicolon-separated HTTP(S) origins.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
