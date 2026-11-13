using SkyRoof;
using System;
using System.Windows.Forms;
using Xunit;

namespace VE3NEA.Dsp.Tests;

public sealed class SatellitePassesPanelRefreshTests
{
  [Theory]
  [InlineData("group-a", "group-a", true)]
  [InlineData("group-a", "group-b", false)]
  [InlineData(null, "group-a", true)]
  public void SameGroupKeepsPublishedSnapshotDuringBackgroundPrediction(
    string? displayed, string requested, bool retain)
  {
    Assert.Equal(retain,
      MainForm.CanRetainGroupPassesDuringRefresh(displayed, requested));
  }

  [Fact]
  public void IdenticalPassReferencesDoNotRecreateNativeVirtualRows()
  {
    var first = new object();
    var second = new object();
    var rows = new[]
    {
      new ListViewItem { Tag = first },
      new ListViewItem { Tag = second }
    };

    Assert.True(PassesPanel.SamePassSequence(rows, new[] { first, second }));
    Assert.False(PassesPanel.SamePassSequence(rows, new[] { second, first }));
    Assert.False(PassesPanel.SamePassSequence(rows, new[] { first }));
    // Different prediction snapshots can carry the same orbit numbers,
    // but must replace their rows because their paths/coordinates changed.
    Assert.False(PassesPanel.SamePassSequence(rows, new[] { new object(), second }));
  }

  [Theory]
  [InlineData(100, 20, 240, 48, 20, 26)]
  [InlineData(100, 95, 240, 48, 95, 99)]
  [InlineData(5, 3, 240, 48, 3, 4)]
  [InlineData(10, -3, 0, 48, 0, 1)]
  public void CountdownRedrawTouchesOnlyVisibleRows(
    int total, int firstRow, int height, int rowHeight,
    int expectedFirst, int expectedLast)
  {
    var result = PassesPanel.VisibleRedrawRange(
      total, firstRow, height, rowHeight);
    Assert.Equal(expectedFirst, result.First);
    Assert.Equal(expectedLast, result.Last);
  }
}
