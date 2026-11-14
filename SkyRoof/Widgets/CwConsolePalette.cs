namespace SkyRoof
{
  /// <summary>
  /// Semantic colors for the CW Console V2 view. The waterfall keeps its
  /// dedicated high-contrast palette regardless of these surface colors.
  /// </summary>
  internal static class CwConsolePalette
  {
    private static GitHubPalette? GitHub =>
      Theme.IsGitHub
        ? (Theme.IsDark ? GitHubThemeColors.Dark : GitHubThemeColors.Light)
        : null;

    internal static Color Surface =>
      GitHub?.CanvasSubtle ??
      (Theme.IsDark ? Color.FromArgb(26, 30, 35) : Color.FromArgb(246, 248, 250));

    internal static Color Card =>
      GitHub?.CanvasDefault ??
      (Theme.IsDark ? Color.FromArgb(35, 41, 48) : Color.White);

    internal static Color Text =>
      GitHub?.TextPrimary ??
      (Theme.IsDark ? Color.Gainsboro : Color.FromArgb(36, 43, 50));

    internal static Color SubtleText =>
      GitHub?.TextSecondary ??
      (Theme.IsDark ? Color.Silver : Color.FromArgb(88, 96, 105));

    internal static Color Border =>
      GitHub?.BorderDefault ??
      (Theme.IsDark ? Color.FromArgb(72, 80, 88) : Color.FromArgb(212, 220, 228));

    internal static Color Selected =>
      GitHub?.AccentMuted ??
      (Theme.IsDark ? Color.FromArgb(28, 55, 78) : Color.FromArgb(220, 238, 250));

    internal static Color Accent =>
      GitHub?.Accent ?? Theme.QsoFieldEdited;

    internal static Color Warning =>
      GitHub?.Attention ??
      (Theme.IsDark ? Color.FromArgb(235, 180, 80) : Color.FromArgb(143, 91, 16));

    internal static Color Provisional =>
      GitHub?.Attention ??
      (Theme.IsDark ? Color.FromArgb(224, 168, 105) : Color.FromArgb(155, 93, 32));

    internal static Color Danger =>
      GitHub?.Danger ??
      (Theme.IsDark ? Color.FromArgb(238, 100, 100) : Color.Firebrick);
  }
}
