using System.IO.Compression;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class OrbitSourceMirrorTests
{
    private const string OmmJson = """
        [{"OBJECT_NAME":"TEST-6DIGIT","OBJECT_ID":"2026-001A","EPOCH":"2026-11-16T00:00:00.000000",
          "MEAN_MOTION":15.5,"ECCENTRICITY":0.001,"INCLINATION":51.6,
          "RA_OF_ASC_NODE":120.0,"ARG_OF_PERICENTER":80.0,"MEAN_ANOMALY":25.0,
          "EPHEMERIS_TYPE":0,"CLASSIFICATION_TYPE":"U","NORAD_CAT_ID":100001,
          "ELEMENT_SET_NO":999,"REV_AT_EPOCH":12345,"BSTAR":0.0001,
          "MEAN_MOTION_DOT":0.00001,"MEAN_MOTION_DDOT":0.0}]
        """;

    [Fact]
    public async Task FirstValidatedMirrorWins_AndInvalidPrimaryNeverOverwritesGoodCache()
    {
        string dir = Path.Combine(Path.GetTempPath(), "skyroof-orbit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string bad = Path.Combine(dir, "server-error.json");
            string valid = Path.Combine(dir, "satellites.json");
            string cache = Path.Combine(dir, "cache.json");
            await File.WriteAllTextAsync(bad, "<html>Cloud mirror temporarily unavailable</html>");
            await File.WriteAllTextAsync(valid, "[{\"sat_id\":\"A\",\"norad_cat_id\":25544}]");
            await File.WriteAllTextAsync(cache, "[{\"sat_id\":\"OLD\"}]");
            using var http = new HttpClient();
            var loaded = await OrbitSourceFetcher.GetTextAsync(
                http, bad + ";" + valid, 5,
                text => OrbitSourceFetcher.LooksLikeSatnogsArray(text, "sat_id"),
                CancellationToken.None);
            Assert.Equal(valid, loaded.Source);
            Assert.Contains("25544", loaded.Content);
            Assert.Equal("[{\"sat_id\":\"OLD\"}]", await File.ReadAllTextAsync(cache));
            OrbitSourceFetcher.WriteTextAtomically(cache, loaded.Content);
            Assert.Equal(loaded.Content, await File.ReadAllTextAsync(cache));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                OrbitSourceFetcher.GetTextAsync(http, bad, 5,
                    text => OrbitSourceFetcher.LooksLikeSatnogsArray(text, "sat_id"),
                    CancellationToken.None));
            Assert.Equal(loaded.Content, await File.ReadAllTextAsync(cache));
            Assert.False(OrbitSourceFetcher.LooksLikeSatnogsArray("[]", "sat_id"));
            Assert.False(OrbitSourceFetcher.LooksLikeSatnogsArray(
                "[{\"message\":\"too many requests\"}]", "sat_id"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ConfiguredPriorityIsNotTheSameAsFallbackMirrorOrder()
    {
        Assert.Equal(new[] { "preferred", "secondary", "local" },
            OrbitSourceFetcher.Candidates("preferred;secondary\nlocal"));
        Assert.Equal(new[] { "local", "secondary", "preferred" },
            SatnogsDb.GetCustomTleApplyOrder("preferred;secondary\nlocal"));
    }

    [Fact]
    public void CelestrakCacheKeyChangesWhenOperatorChangesPriorityOrGroup()
    {
        string amateurCsv = OrbitSourceSettings.DefaultCelestrakOmmCsvUrl;
        string amateurJson = amateurCsv.Replace("FORMAT=CSV", "FORMAT=JSON");
        Assert.Equal(16, OrbitSourceFetcher.CacheKey(amateurCsv).Length);
        Assert.Equal(OrbitSourceFetcher.CacheKey(amateurCsv),
            OrbitSourceFetcher.CacheKey(amateurCsv + "  "));
        Assert.NotEqual(OrbitSourceFetcher.CacheKey(amateurCsv),
            OrbitSourceFetcher.CacheKey(amateurJson));
        Assert.NotEqual(OrbitSourceFetcher.CacheKey(amateurCsv + ";" + amateurJson),
            OrbitSourceFetcher.CacheKey(amateurJson + ";" + amateurCsv));
    }

    [Fact]
    public void SixDigitCelestrakOmmJsonIsDetectedEvenWhenConfiguredAsCsv()
    {
        var database = new SatnogsDb();
        var rows = database.ParseTleContent(OmmJson, ".csv", "CelesTrak alternative");
        var row = Assert.Single(rows);
        Assert.Equal(100001, row.norad_cat_id);
        Assert.NotNull(row.omm);
        Assert.Equal(15.5, row.omm!.MeanMotion, 6);

        Assert.Throws<InvalidDataException>(() =>
            database.ParseTleContent("<html>502 upstream</html>", ".csv", "bad mirror"));
    }

    [Fact]
    public async Task SatYAMLZipMirrorRejectsHtmlAndPreservesOnlyRealArchive()
    {
        string dir = Path.Combine(Path.GetTempPath(), "skyroof-yaml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string invalid = Path.Combine(dir, "invalid.zip");
            string valid = Path.Combine(dir, "valid.zip");
            await File.WriteAllTextAsync(invalid, "HTTP 502");
            await using (var f = File.Create(valid))
            using (var zip = new ZipArchive(f, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("gr-satellites-main/python/satyaml/AO-73.yml");
                await using var writer = entry.Open();
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes("norad: 39444\n");
                await writer.WriteAsync(bytes);
            }
            using var http = new HttpClient();
            byte[] data = await OrbitSourceFetcher.GetZipAsync(
                http, invalid + ";" + valid, 5, CancellationToken.None);
            Assert.True(OrbitSourceFetcher.LooksLikeSatyamlZip(data));
            Assert.False(OrbitSourceFetcher.LooksLikeSatyamlZip(
                System.Text.Encoding.UTF8.GetBytes("HTTP 502")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
