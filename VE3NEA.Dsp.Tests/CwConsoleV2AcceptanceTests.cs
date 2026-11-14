using FluentAssertions;
using SkyRoof;
using SkyRoof.CW;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class CwConsoleV2AcceptanceTests
  {
    private static readonly DateTime T0 =
      new(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void RecordedStyleSnapshot_AllEightSlotsRetainIndependentDecodedText(
      int activeLaneCount)
    {
      var memory = new CwConsoleSlotTextMemory(8);
      var map = new CwConsoleLaneSlotMap(8, TimeSpan.FromSeconds(1));
      CwSignalTrack[] tracks = Enumerable.Range(1, activeLaneCount)
        .Select(i => Track(i, 100 + i, 500 + i * 80))
        .ToArray();
      var transcripts = tracks.ToDictionary(
        CwConsolePresentation.Identity,
        track => Snapshot(track, $"CQ DE K{track.Id}ABC"));

      var live = memory.Update(map.Update(tracks, T0), transcripts);
      live.Should().HaveCount(8);
      live.Count(d => d.Slot.Present).Should().Be(activeLaneCount);

      // A new signal at another AF frequency must not erase older text.
      var shifted = tracks.Select(t => t with
      {
        FrequencyHz = 1900 - t.Id * 80
      }).ToArray();
      var crossing = memory.Update(
        map.Update(shifted, T0.AddSeconds(1)), transcripts);
      foreach (CwConsoleSlotDisplay display in crossing.Where(d => d.Slot.Present))
      {
        CwSignalTrack track = display.Slot.Track!.Value;
        display.Transcript!.Text.Should().Contain($"K{track.Id}ABC");
      }

      // After all tracks expire, the operator can still copy old messages.
      var retained = memory.Update(
        map.Update(Array.Empty<CwSignalTrack>(), T0.AddSeconds(4)), transcripts);
      retained.Count(d => d.Transcript != null).Should().Be(activeLaneCount);
      memory.Clear(0);
      memory.Get(0).Should().BeNull();
      for (int i = 1; i < activeLaneCount; i++)
        memory.Get(i)!.Text.Should().Contain($"K{i + 1}ABC");
    }

    [Fact]
    public void PerSlotClearAndHoldControlsRemainDistinctFromTx()
    {
      using var card = new CwPileupLaneCard(0);
      var track = Track(1, 101, 720);
      var slot = new CwConsoleLaneSlot(
        0, CwConsolePresentation.Identity(track), track, true);
      card.UpdateLane(slot, Snapshot(track, "CQ DE BG5JSU"), null);
      card.ClearButton.Enabled.Should().BeTrue();
      card.CopyText.Should().Contain("BG5JSU");

      var buttons = card.Controls.OfType<System.Windows.Forms.TableLayoutPanel>()
        .Single().Controls.OfType<System.Windows.Forms.Button>().ToArray();
      buttons.Select(x => x.Text).Should()
        .BeEquivalentTo("Copy", "Clear", "HOLD");
      card.UpdateLane(slot, Snapshot(track, "CQ DE BG5JSU"), null, held: true);
      buttons.Select(x => x.Text).Should()
        .BeEquivalentTo("Copy", "Clear", "RELEASE");
    }

    private static CwSignalTrack Track(int id, int hint, double frequency) =>
      new(Id: id, FrequencyHz: frequency, SnrDb: 9,
        DriftHzPerSecond: 0.4, FirstSeenUtc: T0, LastSeenUtc: T0,
        Confirmed: true, Active: true, Ambiguous: false,
        FrequencySigmaHz: 1.5, MergeGroupId: 0,
        IdentityConfidence: 0.88, AssociationHintId: hint);

    private static CwTranscriptSnapshot Snapshot(
      CwSignalTrack track, string text) =>
      new(track.Id, track.AssociationHintId, text, "",
        Array.Empty<CwTranscriptSymbol>(), T0);
  }
}
