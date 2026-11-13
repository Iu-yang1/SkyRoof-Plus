using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SkyRoof;
using SkyRoof.NativeRotator;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class YaesuG5500Tests
{
  private static string Command(byte[]? bytes) => Encoding.ASCII.GetString(bytes!);

  [Fact]
  public void G5500SelectionOnlyChangesOperatorPanel()
  {
    var settings = new RotatorSettings();
    Assert.False(MainForm.UsesYaesuG5500Panel(settings));
    Assert.Equal(RotatorProtocolKind.Gs232, settings.NativeProtocol);
    Assert.Equal(450, settings.MaxAzimuth);
    Assert.Equal(180, settings.MaxElevation);

    settings.HardwareModel = RotatorHardwareModel.YaesuG5500;
    Assert.True(MainForm.UsesYaesuG5500Panel(settings));
    Assert.Equal(RotatorDriverMode.Native, settings.DriverMode);
    Assert.Equal(RotatorProtocolKind.Gs232, settings.NativeProtocol);

    settings.DriverMode = RotatorDriverMode.LegacyRotctld;
    Assert.True(MainForm.UsesYaesuG5500Panel(settings));
    Assert.False(settings.ResolveUseNativeDriver());
  }

  [Fact]
  public void G5500ProfileSurvivesSettingsRoundTripWithoutMutatingConnection()
  {
    var original = new RotatorSettings
    {
      HardwareModel = RotatorHardwareModel.YaesuG5500,
      NativeProtocol = RotatorProtocolKind.Gs232,
      NativeSerialPort = "COM17",
      NativeBaudRate = 4800,
      Host = "192.168.1.20",
      Port = 5600
    };
    var saved = JsonConvert.SerializeObject(original);
    var restored = JsonConvert.DeserializeObject<RotatorSettings>(saved)!;
    Assert.Equal(RotatorHardwareModel.YaesuG5500, restored.HardwareModel);
    Assert.Equal(RotatorProtocolKind.Gs232, restored.NativeProtocol);
    Assert.Equal("COM17", restored.NativeSerialPort);
    Assert.Equal(4800, restored.NativeBaudRate);
    Assert.Equal("192.168.1.20", restored.Host);
    Assert.Equal((ushort)5600, restored.Port);
  }

  [Fact]
  public void G5500UsesExistingGs232AzElCommands()
  {
    const RotatorProtocolKind protocol = RotatorProtocolKind.Gs232;
    Assert.True(NativeRotatorCodec.Supports(protocol, RotatorTransportKind.Serial));
    Assert.True(NativeRotatorCodec.Supports(protocol, RotatorTransportKind.Tcp));
    Assert.True(NativeRotatorCodec.CanJog(protocol));
    Assert.Equal("W450 180\r",
      Command(NativeRotatorCodec.Point(protocol, new RotatorPosition(450, 180))));
    Assert.Equal("C2\r", Command(NativeRotatorCodec.Query(protocol)));
    Assert.Equal("L\r", Command(NativeRotatorCodec.Jog(RotatorDirection.Left, protocol)));
    Assert.Equal("R\r", Command(NativeRotatorCodec.Jog(RotatorDirection.Right, protocol)));
    Assert.Equal("U\r", Command(NativeRotatorCodec.Jog(RotatorDirection.Up, protocol)));
    Assert.Equal("D\r", Command(NativeRotatorCodec.Jog(RotatorDirection.Down, protocol)));
    Assert.Equal("S\r", Command(NativeRotatorCodec.Stop(protocol)));
  }

  [Theory]
  [InlineData("+0450+0180\r", 450, 180)]
  [InlineData("+0000+0000\r", 0, 0)]
  [InlineData("AZ=450 EL=180\r", 450, 180)]
  [InlineData("AZ=225 EL=090\r", 225, 90)]
  public void G5500Gs232AAndBFeedbackRemainCompatible(
    string raw, double az, double el)
  {
    Assert.True(NativeRotatorCodec.TryParse(
      RotatorProtocolKind.Gs232, Encoding.ASCII.GetBytes(raw), out var position));
    Assert.Equal(az, position.Azimuth);
    Assert.Equal(el, position.Elevation);
  }

  [Fact]
  public void G5500PanelShowsFullMechanicalScale()
  {
    Assert.Equal("450.0°   (0–450°)", YaesuG5500ControlCard.FormatAxis(450, 450));
    Assert.Equal("180.0°   (0–180°)", YaesuG5500ControlCard.FormatAxis(180, 180));
    Assert.Contains("---", YaesuG5500ControlCard.FormatAxis(null, 450));
  }

  [Fact]
  public void ProfileDoesNotBypassConfiguredMechanicalRanges()
  {
    var config = new RotatorSettings
    {
      HardwareModel = RotatorHardwareModel.YaesuG5500,
      MinAzimuth = 20, MaxAzimuth = 430,
      MinElevation = 0, MaxElevation = 95
    };
    var target = RotatorWidget.ClampManualTarget(450, 140, config);
    Assert.Equal(430, target.AzimuthDeg);
    Assert.Equal(95, target.ElevationDeg);
  }
}
