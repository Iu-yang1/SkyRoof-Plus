using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SkyRoof.Remote.Station;

internal static class EopChecks
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "skyroof-eop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "fixture-finals.all");
        var cache = Path.Combine(directory, "finals2000A.all");
        var previousAuto = Environment.GetEnvironmentVariable("SKYROOF_OREKIT_EOP_AUTO");
        var previousSources = Environment.GetEnvironmentVariable("SKYROOF_OREKIT_EOP_SOURCES");
        try
        {
            var contents = new StringBuilder();
            for (var i = 0; i < 1040; i++)
                contents.Append("17  1  1 ").Append(58000 + i).Append(".00")
                    .Append(' ', 505).Append('\n');
            await File.WriteAllTextAsync(source, contents.ToString());
            Environment.SetEnvironmentVariable("SKYROOF_OREKIT_EOP_AUTO", "true");
            Environment.SetEnvironmentVariable("SKYROOF_OREKIT_EOP_SOURCES", source);
            await StationOrekitEopBootstrap.RefreshAsync(directory, NullLogger.Instance);
            Ensure(File.Exists(cache) &&
                File.ReadAllText(cache) == File.ReadAllText(source) &&
                File.Exists(Path.Combine(directory, "finals2000A.manifest.json")),
                "Valid offline IERS source failed atomic cache installation");

            File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-8));
            await File.WriteAllTextAsync(source, "<html>HTTP 200 error body</html>");
            await StationOrekitEopBootstrap.RefreshAsync(directory, NullLogger.Instance);
            Ensure(File.ReadAllText(cache) == contents.ToString(),
                "Malformed IERS source overwrote last known-good cache");

            Environment.SetEnvironmentVariable("SKYROOF_OREKIT_EOP_AUTO", "false");
            File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-8));
            await StationOrekitEopBootstrap.RefreshAsync(directory, NullLogger.Instance);
            Ensure(File.ReadAllText(cache) == contents.ToString(),
                "Disabled network refresh modified existing EOP cache");
            Console.WriteLine("PASS: IERS EOP offline source, hash manifest, atomic update, corrupt source rejection and cached fallback");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SKYROOF_OREKIT_EOP_AUTO", previousAuto);
            Environment.SetEnvironmentVariable("SKYROOF_OREKIT_EOP_SOURCES", previousSources);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Ensure(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }
}
