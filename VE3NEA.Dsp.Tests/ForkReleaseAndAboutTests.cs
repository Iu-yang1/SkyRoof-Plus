using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SkyRoof;
using VE3NEA;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class ForkReleaseAndAboutTests
{
  [Fact]
  public void UpdaterUsesForkReleaseApiRatherThanOriginalWebPage()
  {
    Assert.Equal(
      "https://api.github.com/repos/Iu-yang1/SkyRoof-Plus/releases?per_page=100",
      VersionChecker.ReleasesApiUrl);
  }

  [Fact]
  public void NewestAvailableForkInstallerMayBeAPrerelease()
  {
    const string json = """
      [
        {"draft":false,"prerelease":false,"tag_name":"v.1.68",
         "html_url":"https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.68",
         "assets":[{"name":"SkyRoofSetup-v.1.68.exe"}]},
        {"draft":true,"prerelease":true,"tag_name":"v.1.70",
         "html_url":"https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.70",
         "assets":[{"name":"SkyRoofSetup-v.1.70.exe"}]},
        {"draft":false,"prerelease":true,"tag_name":"v.1.69",
         "html_url":"https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.69",
         "assets":[{"name":"SkyRoofSetup-v.1.69.exe"}]}
      ]
      """;

    LatestVersionInfo? latest = VersionChecker.ParseLatestReleaseJson(json);

    Assert.NotNull(latest);
    Assert.True(latest!.Known);
    Assert.Equal(1, latest.Major);
    Assert.Equal(69, latest.Minor);
    Assert.Equal("SkyRoof-Plus v.1.69", latest.Name);
    Assert.Equal("https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.69",
      latest.Url);
  }

  [Fact]
  public void DraftsAndReleasesWithoutInstallerAreNotOffered()
  {
    const string json = """
      [
        {"draft":false,"prerelease":true,"tag_name":"v.1.70",
         "html_url":"https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.70",
         "assets":[]},
        {"draft":true,"prerelease":true,"tag_name":"v.1.71",
         "html_url":"https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/v.1.71",
         "assets":[{"name":"SkyRoofSetup-v.1.71.exe"}]},
        {"draft":false,"prerelease":false,"tag_name":"1.69",
         "html_url":"https://github.com/Iu-yang1/SkyRoof-Plus/releases/tag/1.69",
         "assets":[{"name":"SkyRoofSetup-1.69.exe"}]}
      ]
      """;

    Assert.Null(VersionChecker.ParseLatestReleaseJson(json));
  }

  [Fact]
  public void AboutRetainsOriginalCopyrightAndDisplaysForkAttribution()
  {
    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try
      {
        using var about = new AboutBox();
        var labels = about.Controls.OfType<Label>().Select(label => label.Text).ToArray();
        Assert.Contains(labels, label => label.Contains("Alex Shovkoplyas VE3NEA"));
        Assert.Contains(labels, label => label.Contains("Fork modifications © 2026 Iu-yang1 BG5JSU"));
        Assert.Contains(labels, label => label.Contains("Original project"));
        Assert.Contains(labels, label => label.Contains("SkyRoof-Plus / Fork"));
        Assert.Contains("Alex Shovkoplyas", Utils.GetCopyrightString());
      }
      catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
    if (failure != null)
      throw new Xunit.Sdk.XunitException(failure.ToString());
  }
}
