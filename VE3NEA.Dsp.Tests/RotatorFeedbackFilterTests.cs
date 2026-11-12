using System;
using SkyRoof;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class RotatorFeedbackFilterTests
{
  private static SkyRoof.Bearing At(double az,double el) =>
    new(az*Math.PI/180,el*Math.PI/180);

  [Fact]
  public void OutlierPreservesLastTrustedBearingAndWarnsOnlyAffectedAxis()
  {
    var filter=new RotatorFeedbackFilter(30);
    DateTime t=new(2026,11,12,0,0,0,DateTimeKind.Utc);
    Assert.True(filter.Observe(At(90,30),t,null));
    Assert.False(filter.Observe(At(240,30),t.AddMilliseconds(200),At(95,30)));
    Assert.True(filter.IsDegraded);
    Assert.True(filter.AzimuthSuspect);
    Assert.False(filter.ElevationSuspect);
    Assert.Equal(90,filter.Trusted!.AzDeg,2);
    Assert.Equal(t,filter.LastTrustedUtc);
    Assert.Equal(240,filter.Rejected!.AzDeg,2);
    Assert.False(filter.Observe(At(245,31),t.AddMilliseconds(400),At(97,30)));
    Assert.Equal(90,filter.Trusted!.AzDeg,2);
  }

  [Fact]
  public void TwoConsistentValidSamplesRestoreFeedbackAutomatically()
  {
    var filter=new RotatorFeedbackFilter(30);
    DateTime t=new(2026,11,12,0,0,0,DateTimeKind.Utc);
    filter.Observe(At(92,45),t,null);
    Assert.False(filter.Observe(At(235,45),t.AddMilliseconds(200),At(99,45)));
    Assert.False(filter.Observe(At(94,45),t.AddMilliseconds(400),At(101,45)));
    Assert.True(filter.IsDegraded);
    Assert.True(filter.Observe(At(95,45),t.AddMilliseconds(600),At(102,45)));
    Assert.False(filter.IsDegraded);
    Assert.Null(filter.Rejected);
    Assert.Equal(95,filter.Trusted!.AzDeg,2);
  }

  [Fact]
  public void MovingBeyondFrozenReferenceCanRecoverNearCurrentCommand()
  {
    var filter=new RotatorFeedbackFilter(30);
    DateTime t=new(2026,11,12,0,0,0,DateTimeKind.Utc);
    filter.Observe(At(92,40),t,null);
    filter.Observe(At(230,40),t.AddMilliseconds(200),At(120,40));
    Assert.False(filter.Observe(At(180,40),t.AddSeconds(2),At(182,40)));
    Assert.True(filter.Observe(At(181,40),t.AddSeconds(2.3),At(183,40)));
    Assert.False(filter.IsDegraded);
    Assert.Equal(181,filter.Trusted!.AzDeg,2);
  }

  [Fact]
  public void ConsistentButOffTargetOutliersNeverBecomeTrusted()
  {
    var filter=new RotatorFeedbackFilter(30);
    DateTime t=new(2026,11,12,0,0,0,DateTimeKind.Utc);
    filter.Observe(At(90,40),t,null);
    for(int i=0;i<8;i++)
      Assert.False(filter.Observe(At(230+i,40),t.AddMilliseconds(200*(i+1)),At(105,40)));
    Assert.True(filter.IsDegraded);
    Assert.Equal(90,filter.Trusted!.AzDeg,2);
  }

  [Fact]
  public void CircularAzimuthAndExtendedMechanicalTurnsAreDistinguished()
  {
    var circular=new RotatorFeedbackFilter(30);
    DateTime t=new(2026,11,12,0,0,0,DateTimeKind.Utc);
    circular.Observe(At(359,10),t,null);
    Assert.True(circular.Observe(At(1,10),t.AddMilliseconds(200),null));
    Assert.False(circular.IsDegraded);

    var multiTurn=new RotatorFeedbackFilter(30);
    multiTurn.Observe(At(450,10),t,null);
    Assert.False(multiTurn.Observe(At(90,10),t.AddMilliseconds(200),null));
    Assert.True(multiTurn.AzimuthSuspect);
  }

  [Fact]
  public void ElevationOutlierMarksOnlyElevation()
  {
    var filter=new RotatorFeedbackFilter(30);
    DateTime t=new(2026,11,12,0,0,0,DateTimeKind.Utc);
    filter.Observe(At(100,20),t,null);
    Assert.False(filter.Observe(At(101,65),t.AddMilliseconds(200),null));
    Assert.False(filter.AzimuthSuspect);
    Assert.True(filter.ElevationSuspect);
  }
}
