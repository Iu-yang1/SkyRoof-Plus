using System.Text;

namespace SkyRoof.CW
{
  internal readonly record struct CwConsoleSlotDisplay(
    CwConsoleLaneSlot Slot,
    CwTranscriptSnapshot? Transcript,
    bool Held);

  /// <summary>
  /// Text is owned by a fixed operator slot, not by the transient tracked
  /// carrier. A new H/T identity starts a new labelled section in that
  /// slot; neither signal loss nor reuse clears earlier decoded messages.
  /// </summary>
  internal sealed class CwConsoleSlotTextMemory
  {
    private sealed class SlotState
    {
      internal CwConsoleLaneIdentity? Identity;
      internal CwConsoleLaneIdentity? LastDisplayIdentity;
      internal CwSignalTrack? LastTrack;
      internal CwTranscriptSnapshot? Latest;
      internal string History = string.Empty;
      internal int HiddenCharacters;
    }

    private readonly SlotState[] states;

    internal CwConsoleSlotTextMemory(int slotCount = 8)
    {
      states = Enumerable.Range(0, slotCount)
        .Select(_ => new SlotState()).ToArray();
    }

    internal IReadOnlyList<CwConsoleSlotDisplay> Update(
      IReadOnlyList<CwConsoleLaneSlot> slots,
      IReadOnlyDictionary<CwConsoleLaneIdentity, CwTranscriptSnapshot> transcripts,
      Func<int, bool>? isHeld = null)
    {
      var result = new CwConsoleSlotDisplay[slots.Count];
      for (int i = 0; i < slots.Count; i++)
      {
        CwConsoleLaneSlot current = slots[i];
        SlotState state = states[current.Index];
        if (current.Identity is CwConsoleLaneIdentity id)
        {
          if (state.Identity != id)
            StartNewIdentity(state, id);

          state.LastTrack = current.Track;
          if (transcripts.TryGetValue(id, out CwTranscriptSnapshot? latest) &&
              (state.Latest == null ||
               latest.LastWindowEndUtc >= state.Latest.LastWindowEndUtc))
            state.Latest = latest;
        }

        CwTranscriptSnapshot? visible = Snapshot(state);
        CwConsoleLaneSlot shown = current;
        if (!current.Identity.HasValue && visible != null)
          shown = new CwConsoleLaneSlot(
            current.Index, state.LastDisplayIdentity, state.LastTrack, false);

        result[i] = new CwConsoleSlotDisplay(
          shown, visible, isHeld?.Invoke(current.Index) ?? false);
      }
      return result;
    }

    internal void NewTimeline()
    {
      foreach (SlotState state in states)
      {
        // A decoder timeline may reuse H/T numeric IDs. Close the old
        // section before seeing any new track so old and new stations
        // can never share a transcript just because IDs were recycled.
        if (state.Identity.HasValue)
          StartNewIdentity(state, default);
        state.Identity = null;
      }
    }

    internal void Clear(int index)
    {
      SlotState state = states[index];
      state.History = string.Empty;
      state.HiddenCharacters = state.Latest?.Text.Length ?? 0;
      // The latest decoder snapshot stays available as a watermark so the
      // next 250-ms refresh does not resurrect the cleared characters.
    }

    internal CwTranscriptSnapshot? Get(int index) =>
      Snapshot(states[index]);

    internal string ConfirmedText(int index)
    {
      SlotState state = states[index];
      CwTranscriptSnapshot? snapshot = state.Latest;
      if (snapshot == null ||
          state.HiddenCharacters >= snapshot.CommittedText.Length)
        return string.Empty;
      return snapshot.CommittedText[state.HiddenCharacters..];
    }

    private static void StartNewIdentity(
      SlotState state, CwConsoleLaneIdentity next)
    {
      if (state.Identity.HasValue && state.Latest != null)
      {
        string old = VisibleCurrent(state);
        if (old.Length > 0)
        {
          string suffix = state.Latest.ProvisionalText.Length > 0
            ? " (unconfirmed suffix may be present)"
            : string.Empty;
          state.History +=
            $"[{state.Identity.Value} retained{suffix}] {old}\r\n";
        }
      }
      state.Identity = next;
      state.LastDisplayIdentity = next;
      state.Latest = null;
      state.HiddenCharacters = 0;
    }

    private static string VisibleCurrent(SlotState state)
    {
      if (state.Latest is not CwTranscriptSnapshot snapshot)
        return string.Empty;
      int offset = Math.Min(state.HiddenCharacters, snapshot.Text.Length);
      return snapshot.Text[offset..];
    }

    private static CwTranscriptSnapshot? Snapshot(SlotState state)
    {
      string committed = state.History;
      string provisional = string.Empty;
      CwTranscriptSnapshot? latest = state.Latest;
      if (latest != null)
      {
        int skip = state.HiddenCharacters;
        if (skip < latest.CommittedText.Length)
          committed += latest.CommittedText[skip..];
        if (skip < latest.Text.Length)
          provisional = latest.ProvisionalText[
            Math.Max(0, skip - latest.CommittedText.Length)..];
      }

      if (committed.Length == 0 && provisional.Length == 0)
        return null;

      return new CwTranscriptSnapshot(
        latest?.TrackId ?? 0,
        latest?.AssociationHintId ?? 0,
        committed,
        provisional,
        Array.Empty<CwTranscriptSymbol>(),
        latest?.LastWindowEndUtc ?? DateTime.MinValue);
    }
  }
}
