using System.Globalization;
using System.Resources;

namespace SkyRoof
{
  /// <summary>
  /// UI-only string resources. Changes never touch radio/DSP state or
  /// CurrentCulture (which controls protocol number parsing).
  /// </summary>
  internal static class UiLocalizationService
  {
    private static readonly ResourceManager Strings =
      new("SkyRoof.Localization.UiStrings", typeof(UiLocalizationService).Assembly);

    internal static UiLanguage Current => SettingsLanguage.Current;
    internal static event Action? LanguageChanged;

    internal static CultureInfo CurrentResourceCulture =>
      Current == UiLanguage.SimplifiedChinese
        ? CultureInfo.GetCultureInfo("zh-CN")
        : CultureInfo.GetCultureInfo("en-US");

    internal static string Get(string key) =>
      Strings.GetString(key, CurrentResourceCulture) ??
      Strings.GetString(key, CultureInfo.GetCultureInfo("en-US")) ??
      key;

    internal static string Format(string key, params object?[] args) =>
      string.Format(CultureInfo.CurrentCulture, Get(key), args);

    internal static string? Find(string key) =>
      Strings.GetString(key, CurrentResourceCulture);

    /// <summary>Translate only controls with explicitly registered resource keys.
    /// Live telemetry and user-entered content have no keys, so are not rewritten.</summary>
    internal static void ApplyStaticControls(Control root)
    {
      string section = root.GetType().Name;
      if (root is Form &&
          Find($"Dialog.{section}.Title") is string windowTitle)
        root.Text = windowTitle;
      void Visit(Control control)
      {
        if (!string.IsNullOrEmpty(control.Name) &&
            Find($"Control.{section}.{control.Name}") is string label)
          control.Text = label;
        if (control is ListView list)
          foreach (ColumnHeader column in list.Columns)
            if (Find($"Column.{section}.{column.Name}") is string title)
              column.Text = title;
        if (control is DataGridView grid)
          foreach (DataGridViewColumn column in grid.Columns)
            if (Find($"Column.{section}.{column.Name}") is string title)
              column.HeaderText = title;
        if (control is ToolStrip strip) ApplyToolStripItems(strip.Items, section);
        if (control.ContextMenuStrip is ContextMenuStrip menu)
          ApplyToolStripItems(menu.Items, section);
        foreach (Control child in control.Controls) Visit(child);
      }
      Visit(root);
    }

    internal static void ApplyToolStripItems(ToolStripItemCollection items, string section)
    {
      foreach (ToolStripItem item in items)
      {
        if (Find($"Menu.{section}.{item.Name}") is string label)
          item.Text = label;
        if (item is ToolStripDropDownItem menu)
          ApplyToolStripItems(menu.DropDownItems, section);
      }
    }

    /// <summary>One entry point for language changes; the legacy PropertyGrid
    /// API also triggers NotifyLanguageChanged on a direct Set call.</summary>
    internal static void Apply(UiLanguage language) => SettingsLanguage.Set(language);

    internal static void NotifyLanguageChanged() => LanguageChanged?.Invoke();

    /// <summary>
    /// Subscribe a live WinForms view and automatically release the subscription
    /// on disposal. No control recreation, docking or reconnect occurs.
    /// </summary>
    internal static void Bind(Control view, Action render)
    {
      void Refresh()
      {
        if (view.IsDisposed || view.Disposing) return;
        if (view.InvokeRequired)
        {
          if (view.IsHandleCreated)
            view.BeginInvoke((Action)(() =>
            {
              if (!view.IsDisposed && !view.Disposing) render();
            }));
          return;
        }
        render();
      }
      LanguageChanged += Refresh;
      view.Disposed += (_, _) => LanguageChanged -= Refresh;
      Refresh();
    }
  }
}
