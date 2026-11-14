using FluentAssertions;
using SkyRoof.CW;
using Xunit;

namespace VE3NEA.Dsp.Tests
{
  public sealed class CwConsoleSlotTextMemoryTests
  {
    private static readonly DateTime Origin =
      new(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DecoderTextSurvivesCarrierLossGraceExpiryAndNewFrequency()
    {
      var memory = new CwConsoleSlotTextMemory(2);
      var old = Track(1, 101, 700);
      var next = Track(2, 202, 1300);
      var map = new CwConsoleLaneSlotMap(2, TimeSpan.FromSeconds(1));
      var texts = new Dictionary<CwConsoleLaneIdentity, CwTranscriptSnapshot>
      {
        [CwConsolePresentation.Identity(old)] = Message(old, "CQ BG5JSU", "", 0)
      };

      var first = memory.Update(map.Update(new[] { old }, Origin), texts);
      first[0].Transcript!.Text.Should().Contain("CQ BG5JSU");

      memory.Update(map.Update(Array.Empty<CwSignalTrack>(),
        Origin.AddSeconds(2)), texts)[0].Transcript!.Text
        .Should().Contain("CQ BG5JSU");

      texts[CwConsolePresentation.Identity(next)] =
        Message(next, "K1ABC", " 599", 3);
      var switched = memory.Update(
        map.Update(new[] { next }, Origin.AddSeconds(3)), texts);
      switched[0].Transcript!.Text.Should().Contain("CQ BG5JSU")
        .And.Contain("K1ABC 599");
      switched[0].Transcript!.CommittedText.Should().Contain("H101 retained");
      switched[0].Slot.Track!.Value.FrequencyHz.Should().Be(1300);
    }

    [Fact]
    public void ClearAffectsOnlyRequestedSlotAndDoesNotResurrectOldSnapshot()
    {
      var memory = new CwConsoleSlotTextMemory(2);
      var a = Track(1, 101, 600);
      var b = Track(2, 202, 900);
      var slots = new[]
      {
        new CwConsoleLaneSlot(0, CwConsolePresentation.Identity(a), a, true),
        new CwConsoleLaneSlot(1, CwConsolePresentation.Identity(b), b, true)
      };
      var texts = new Dictionary<CwConsoleLaneIdentity, CwTranscriptSnapshot>
      {
        [CwConsolePresentation.Identity(a)] = Message(a, "CQ DE", "", 1),
        [CwConsolePresentation.Identity(b)] = Message(b, "K1ABC", "", 1)
      };

      memory.Update(slots, texts);
      memory.Clear(0);
      memory.Update(slots, texts)[0].Transcript.Should().BeNull();
      memory.Get(1)!.Text.Should().Be("K1ABC");

      texts[CwConsolePresentation.Identity(a)] =
        Message(a, "CQ DE 599", "", 2);
      memory.Update(slots, texts)[0].Transcript!.Text.Should().Be(" 599");
    }

    [Fact]
    public void ProvisionalToCommittedDoesNotDuplicateWhileRetained()
    {
      var memory = new CwConsoleSlotTextMemory(1);
      var a = Track(1, 101, 700);
      var slots = new[]
      {
        new CwConsoleLaneSlot(0, CwConsolePresentation.Identity(a), a, true)
      };
      var texts = new Dictionary<CwConsoleLaneIdentity, CwTranscriptSnapshot>
      {
        [CwConsolePresentation.Identity(a)] = Message(a, "CQ ", "TEST", 1)
      };
      memory.Update(slots, texts)[0].Transcript!.Text.Should().Be("CQ TEST");
      texts[CwConsolePresentation.Identity(a)] = Message(a, "CQ TEST", "", 2);
      var shown = memory.Update(slots, texts)[0].Transcript!;
      shown.CommittedText.Should().Be("CQ TEST");
      shown.ProvisionalText.Should().BeEmpty();

      memory.Clear(0);
      texts[CwConsolePresentation.Identity(a)] =
        Message(a, "CQ TEST 73", "", 3);
      memory.Update(slots, texts)[0].Transcript!.Text.Should().Be(" 73");
    }

    [Fact]
    public void DecoderRestartDoesNotAutoClearPreviouslyDecodedMessage()
    {
      var memory = new CwConsoleSlotTextMemory(1);
      var track = Track(3, 303, 900);
      var slot = new[]
      {
        new CwConsoleLaneSlot(0, CwConsolePresentation.Identity(track), track, true)
      };
      var data = new Dictionary<CwConsoleLaneIdentity, CwTranscriptSnapshot>
      {
        [CwConsolePresentation.Identity(track)] = Message(track, "CQ TEST", "", 1)
      };
      memory.Update(slot, data);
      data[CwConsolePresentation.Identity(track)] = Message(track, "", "", 2);
      memory.Update(slot, data)[0].Transcript!.Text.Should().Contain("CQ TEST");

      data[CwConsolePresentation.Identity(track)] = Message(track, "K1ABC", "", 3);
      memory.Update(slot, data)[0].Transcript!.Text
        .Should().Contain("CQ TEST").And.Contain("K1ABC");
    }

    [Theory]
    [InlineData("CQ DE BG5JSU", "BG5JSU")]
    [InlineData("K1ABC 599", "K1ABC")]
    [InlineData("CQ CQ", null)]
    [InlineData("5NN 73", null)]
    public void ConfirmedCallsign_IsExplicitAndDoesNotInferFromReports(
      string text, string? expected)
    {
      CwConfirmedCallsign.LastCandidate(text).Should().Be(expected);
    }

    [Fact]
    public void TimelineResetKeepsTextAndPreventsReusedAssociationIdJoiningIt()
    {
      var memory = new CwConsoleSlotTextMemory(1);
      var old = Track(1, 101, 500);
      var oldSlots = new[]
      {
        new CwConsoleLaneSlot(0, CwConsolePresentation.Identity(old), old, true)
      };
      var texts = new Dictionary<CwConsoleLaneIdentity, CwTranscriptSnapshot>
      {
        [CwConsolePresentation.Identity(old)] = Message(old, "FIRST", "", 1)
      };
      memory.Update(oldSlots, texts);
      memory.NewTimeline();
      memory.Get(0)!.Text.Should().Contain("FIRST");

      var next = Track(9, 101, 1500);
      texts[CwConsolePresentation.Identity(next)] =
        Message(next, "SECOND", "", 3);
      var shown = memory.Update(new[]
      {
        new CwConsoleLaneSlot(0, CwConsolePresentation.Identity(next), next, true)
      }, texts)[0].Transcript!;
      shown.Text.Should().Contain("FIRST").And.Contain("SECOND");
      shown.CommittedText.Should().Contain("retained");
    }

    private static CwSignalTrack Track(int id, int hint, double hz) =>
      new(
        Id: id,
        FrequencyHz: hz,
        SnrDb: 12,
        DriftHzPerSecond: 0,
        FirstSeenUtc: Origin,
        LastSeenUtc: Origin,
        Confirmed: true,
        Active: true,
        Ambiguous: false,
        FrequencySigmaHz: 1,
        MergeGroupId: 0,
        IdentityConfidence: 0.9,
        AssociationHintId: hint);

    private static CwTranscriptSnapshot Message(
      CwSignalTrack track, string committed, string provisional,
      int seconds) =>
      new(track.Id, track.AssociationHintId, committed, provisional,
        Array.Empty<CwTranscriptSymbol>(), Origin.AddSeconds(seconds));
  }
}
