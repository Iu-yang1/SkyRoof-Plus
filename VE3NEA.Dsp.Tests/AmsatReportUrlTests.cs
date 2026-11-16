using Newtonsoft.Json.Linq;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class AmsatReportApiTests
  {
    [Fact]
    public void ReportPayloadUsesNewAmsatApiAndPreservesUtcClockMargin()
    {
      var timestamp = new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);
      JObject body = JObject.Parse(AmsatStatusLoader.BuildAmsatReportPayload(
        "ISS_[FM Voice]", "Telemetry Only", "bg5jsu", "ol79wq", timestamp));

      Assert.Equal("ISS_[FM Voice]", (string?)body["name"]);
      Assert.Equal("Telemetry Only", (string?)body["report"]);
      Assert.Equal("BG5JSU", (string?)body["callsign"]);
      Assert.Equal("OL79WQ", (string?)body["grid_square"]);
      Assert.Equal("2026-11-05T07:59:00Z", (string?)body["reported_at"]);
    }

    [Fact]
    public void ReportPayloadYearBoundaryAndBlankGrid()
    {
      var timestamp = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      JObject body = JObject.Parse(AmsatStatusLoader.BuildAmsatReportPayload(
        "RS-44", "Heard", "BG5JSU", "", timestamp));

      Assert.Equal("2026-12-31T23:59:00Z", (string?)body["reported_at"]);
      Assert.Null(body["grid_square"]);
    }
  }
}
