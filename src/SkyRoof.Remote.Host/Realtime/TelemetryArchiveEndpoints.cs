using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyRoof.Remote.Host.Realtime;

internal static partial class TelemetryArchiveEndpoints
{
    private const string Route = "/api/v1/dsp/telemetry/archive";

    internal static void MapArchive(WebApplication app)
    {
        app.MapGet(Route, ListAsync).RequireAuthorization();
        app.MapGet(Route + "/{norad:int}/{day}/{stem}/{format}", DownloadAsync).RequireAuthorization();
    }

    private static string Root() => Environment.GetEnvironmentVariable("SKYROOF_TELEMETRY_ARCHIVE_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyRoof", "Remote", "TelemetryDecodes", "Binary");

    [GeneratedRegex(@"\A[0-9]{8}_[0-9]{6}_[0-9]{3}_[0-9]{8,19}(?:_[0-9a-f]{8})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex FrameStem();

    private static bool ValidDay(string? day) =>
        DateOnly.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool ValidStem(string stem, string day) =>
        FrameStem().IsMatch(stem) && stem.StartsWith(day + "_", StringComparison.Ordinal);

    private static string DirectoryFor(int norad, string day) =>
        Path.Combine(Root(), norad.ToString(CultureInfo.InvariantCulture), day);

    private static async Task<IResult> ListAsync(int norad, string day, string? before, int? limit, HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        if (norad <= 0 || !ValidDay(day) || (before is not null && !ValidStem(before, day)) || limit is < 1 or > 200)
            return Results.BadRequest(new { error = "Use NORAD > 0, UTC yyyyMMdd, optional archive cursor and limit 1–200." });
        try
        {
            int count = limit ?? 50;
            string folder = DirectoryFor(norad, day);
            var entries = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.log")
                .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .Where(stem => ValidStem(stem, day) && (before is null || string.CompareOrdinal(stem, before) < 0))
                .Where(stem => File.Exists(Path.Combine(folder, stem + ".bin")))
                .OrderByDescending(stem => stem, StringComparer.Ordinal).Take(count + 1).ToArray() : [];
            var items = entries.Take(count).Select(stem => new
            {
                stem, bytes = new FileInfo(Path.Combine(folder, stem + ".bin")).Length
            }).ToArray();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { norad, day, items, next = entries.Length > count ? entries[count - 1] : null });
        }
        catch (IOException) { return Results.Problem(title: "Archive temporarily unavailable", statusCode: 503); }
        catch (UnauthorizedAccessException) { return Results.Problem(title: "Archive access failed", statusCode: 503); }
    }

    private static async Task<IResult> DownloadAsync(int norad, string day, string stem, string format, HttpContext context)
    {
        if (!await LiveAudioWebSocket.StillAuthorizedAsync(context)) return Results.Forbid();
        if (norad <= 0 || !ValidDay(day) || !ValidStem(stem, day) || format is not ("bin" or "log"))
            return Results.BadRequest();
        string folder = DirectoryFor(norad, day);
        string bin = Path.Combine(folder, stem + ".bin");
        string log = Path.Combine(folder, stem + ".log");
        if (!File.Exists(bin) || !File.Exists(log)) return Results.NotFound();
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(format == "bin" ? bin : log, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(bytes, format == "bin" ? "application/octet-stream" : "text/plain; charset=utf-8", stem + "." + format);
        }
        catch (FileNotFoundException) { return Results.NotFound(); }
        catch (IOException) { return Results.Problem(title: "Archive temporarily unavailable", statusCode: 503); }
        catch (UnauthorizedAccessException) { return Results.Problem(title: "Archive access failed", statusCode: 503); }
    }
}
