using System.Drawing;
using System.Numerics;
using SkyRoof;
using VE3NEA;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class EarthViewCameraTests
{
  [Theory]
  [InlineData(1, -60)]
  [InlineData(2, -30)]
  [InlineData(4, -15)]
  public void HorizontalDragScalesWithZoom(double zoom, double longitude)
  {
    var camera = new EarthViewCamera();
    camera.Reset(new GeoPoint(0, 0));
    Assert.True(camera.Pan(60, 0, new Size(720, 360), zoom));
    Assert.Equal(longitude, camera.Center.Longitude, 8);
    Assert.Equal(0, camera.Center.Latitude, 8);
    Assert.True(camera.IsManual);
  }

  [Fact]
  public void ManualCenterSurvivesUpdatesUntilReset()
  {
    var camera = new EarthViewCamera();
    var home = new GeoPoint(28, 116);
    var satellite = new GeoPoint(55, -80);
    camera.Follow(home);
    camera.Pan(15, 20, new Size(500, 300), 3);
    GeoPoint manual = camera.Center;
    camera.Follow(satellite);
    camera.Follow(home);
    Assert.Equal(manual.Latitude, camera.Center.Latitude);
    Assert.Equal(manual.Longitude, camera.Center.Longitude);
    camera.Reset(satellite);
    Assert.False(camera.IsManual);
    Assert.Equal(satellite.Latitude, camera.Center.Latitude);
    camera.Follow(home);
    Assert.Equal(home.Longitude, camera.Center.Longitude);
  }

  [Fact]
  public void PanCrossesAntimeridianAndPole()
  {
    var camera = new EarthViewCamera();
    camera.Reset(new GeoPoint(0, 179));
    camera.Pan(-4, 0, new Size(360, 360), 1);
    Assert.Equal(-177, camera.Center.Longitude, 8);
    camera.Reset(new GeoPoint(89, 0));
    camera.Pan(0, 3, new Size(360, 360), 1);
    Assert.Equal(88, camera.Center.Latitude, 8);
    Assert.Equal(180, Math.Abs(camera.Center.Longitude), 8);
  }

  [Fact]
  public void ProjectionUsesManualCenterAndAspectRatio()
  {
    var camera = new EarthViewCamera();
    camera.Reset(new GeoPoint(0, 0));
    var satellite = new GeoPoint(0, 30);
    var size = new Size(720, 360);
    Assert.Equal(1f / 12, camera.Project(satellite, size, 1).X, 6);
    camera.Pan(-30, 0, size, 1);
    Assert.Equal(0, camera.Project(satellite, size, 1).X, 6);
    Assert.True(camera.Project(new GeoPoint(0, 0), size, 1).X < 0);
    Assert.Equal(0, camera.Heading(satellite, new GeoPoint(1, 31), size, 1), 2);
  }

  [Fact]
  public void RotatedOffCenterSpriteRetainsGeographicPosition()
  {
    var position = new PointF(0.3f, -0.4f);
    var matrix = Sprite.BuildTransform(position, 1.2f, 0.8f,
      new Size(32, 24), new Size(720, 360));
    Vector3 center = Vector3.Transform(Vector3.Zero, matrix);
    Assert.Equal(position.X, center.X, 6);
    Assert.Equal(position.Y, center.Y, 6);
  }

  [Fact]
  public void EmptyDragDoesNotDisableFollowing()
  {
    var camera = new EarthViewCamera();
    camera.Reset(new GeoPoint(10, 20));
    Assert.False(camera.Pan(0, 0, new Size(300, 300), 1));
    Assert.False(camera.Pan(10, 10, Size.Empty, 1));
    Assert.False(camera.IsManual);
  }
}
