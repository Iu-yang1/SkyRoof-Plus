using Xunit;
using SkyRoof;

namespace VE3NEA.Dsp.Tests;

public sealed class IcomNetworkReadbackTests
{
  [Fact]
  public void ParsesValidPartialReadbackAndPreservesMissingProperties()
  {
    var state = IcomNetworkReadbackState.Parse(new Dictionary<string, string>
    {
      ["IP"] = "192.168.1.50",
      ["CONTROL"] = "50001",
      ["SERIAL"] = "50002",
      ["ENABLED"] = "1"
    });
    Assert.Equal("192.168.1.50", state.IpAddress);
    Assert.Equal(50001, state.ControlPort);
    Assert.Equal(50002, state.SerialPort);
    Assert.Null(state.AudioPort);
    Assert.True(state.NetworkEnabled);
    Assert.Equal(4, state.AvailableCount);
  }

  [Fact]
  public void InvalidReadbackValuesCannotReplaceSettings()
  {
    var state = IcomNetworkReadbackState.Parse(new Dictionary<string, string>
    {
      ["IP"] = "not-an-ip",
      ["CONTROL"] = "70000",
      ["SERIAL"] = "0",
      ["AUDIO"] = "50003",
      ["ENABLED"] = "2"
    });
    Assert.Null(state.IpAddress);
    Assert.Null(state.ControlPort);
    Assert.Null(state.SerialPort);
    Assert.Null(state.NetworkEnabled);
    Assert.Equal(50003, state.AudioPort);
    Assert.Equal(1, state.AvailableCount);
  }
}
