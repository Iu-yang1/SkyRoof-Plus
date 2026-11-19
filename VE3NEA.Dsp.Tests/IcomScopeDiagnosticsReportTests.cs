using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class IcomScopeDiagnosticsReportTests
{
  [Theory]
  [InlineData("Receiving native combined IC-9700 475-bin LAN waveform · Direct LAN.", false)]
  [InlineData("Receiving high-rate native IC-9700 LAN 27 00 waveform · radio", false)]
  [InlineData("Receiving IC-9700 CI-V 27 00 spectrum data · SkyCAT.", false)]
  [InlineData("Direct LAN authenticated/connecting, but no native CI-V waveform has arrived yet.", true)]
  [InlineData("Connection failed.", true)]
  [InlineData("Spectrum stopped.", true)]
  public void OnlyNormalReceptionTextIsHidden(string message, bool visible) =>
    Assert.Equal(visible, IcomScopeDiagnosticsReport.ShowInline(message));

  [Theory]
  [InlineData("OpenGL / Intel", "GPU fresh waveform swaps:", "UI fresh waveform OnPaint completions:")]
  [InlineData("GDI+", "UI fresh waveform OnPaint completions:", "GPU fresh waveform swaps:")]
  public void SummaryUsesActiveRendererAndFullCopyRetainsAllDetails(string renderer, string shown, string hidden)
  {
    const string reception = "Receiving native combined IC-9700 475-bin LAN waveform · Direct LAN.";
    string body = string.Join(Environment.NewLine, "Data source: Direct LAN",
      "Scope renderer: " + renderer, "Complete sweeps captured: 30.0/s",
      "GPU fresh waveform swaps: 30/s", "UI fresh waveform OnPaint completions: 30/s",
      "Native CI-V transport duplicates: 0", "Processing queue dropped sweeps: 0");
    var report = new IcomScopeDiagnosticsReport("Direct LAN · 30/s", reception, body, "Toolbar DPI: 96");
    Assert.Contains(shown, report.Summary);
    Assert.DoesNotContain(hidden, report.Summary);
    Assert.DoesNotContain(reception, report.Summary);
    Assert.DoesNotContain("Toolbar DPI:", report.Summary);
    Assert.Contains(reception, report.FullText);
    Assert.Contains(hidden, report.FullText);
    Assert.Contains("Toolbar DPI: 96", report.FullText);
    Assert.Contains("Native CI-V transport duplicates: 0", report.FullText);
  }

  [Fact]
  public void ErrorRemainsInOverviewAndFullReport()
  {
    var report = new IcomScopeDiagnosticsReport("Offline", "Connection failed.", "", "");
    Assert.Contains("Connection failed.", report.Summary);
    Assert.Contains("Connection failed.", report.FullText);
  }
}
