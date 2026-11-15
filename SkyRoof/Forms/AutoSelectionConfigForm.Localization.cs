namespace SkyRoof
{
  public partial class AutoSelectionConfigForm
  {
    private void RefreshLocalizedTooltips()
    {
      tips.SetToolTip(RotationTree, UiLocalizationService.Get("AutoSelection.TipTree"));
      tips.SetToolTip(MoveUpBtn, UiLocalizationService.Get("AutoSelection.TipMoveUp"));
      tips.SetToolTip(MoveDownBtn, UiLocalizationService.Get("AutoSelection.TipMoveDown"));
      tips.SetToolTip(TransmitterCombo, UiLocalizationService.Get("AutoSelection.TipTransmitter"));
      tips.SetToolTip(RecordCombo, UiLocalizationService.Get("AutoSelection.TipRecord"));
      tips.SetToolTip(SelectAllBtn, UiLocalizationService.Get("AutoSelection.TipSelectAll"));
      tips.SetToolTip(ClearBtn, UiLocalizationService.Get("AutoSelection.TipClear"));
      if (ctx == null) return;
      if (!ctx.Settings.Rotator.Enabled)
        TrackAntennaCheckbox.ForeColor = Color.Red;
      tips.SetToolTip(TrackAntennaCheckbox, UiLocalizationService.Get(
        ctx.Settings.Rotator.Enabled ? "AutoSelection.TipTrack" :
        "AutoSelection.TipTrackDisabled"));
      RecordCombo.Refresh();
    }
  }
}
