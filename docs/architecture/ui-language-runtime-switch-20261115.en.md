# SkyRoof-Plus live English / Simplified Chinese UI

Use **Tools > Language > English / 简体中文** for instant language switching. The choice is saved in `Settings.json` under `Ui.Language`. The Settings language selector supports preview with Apply/OK to commit and Cancel to roll back.

`UiLocalizationService` reads neutral-English `UiStrings.resx` and `UiStrings.zh-CN.resx`, manages view subscriptions and updates open DockPanel titles in place. `SettingsLanguage` continues to serve PropertyGrid. Changing language does not recreate windows, restart CAT/PTT/rotator sessions or reset CW/telemetry/spectrum/recorder pipelines.

Localized workspaces include MainForm navigation, CW RX/TX, FT4 Odd/Even, Recorder and IQ/IF labels, telemetry statuses, Auto Selection, Icom spectrum tooltips, AFC and SSTV tabs, image controls and common statuses. The satellite management and device/configuration dialogs use the same resource catalog. Raw radio/telemetry fields, callsigns, TLE, JSON identifiers and ADIF keys remain untranslated.

**Acceptance:** Switch both ways while connected; verify the saved choice after restart; preview/Cancel in Settings; keep entered fields, selected SSTV templates, decoder state, Dock layout and record mode intact. Test 100–200% DPI in light/dark themes. Windows CI runs xUnit, resource parity and placeholder tests, aislop and installer smoke checks. External driver errors can remain in their native diagnostic language. Hardware integration is a separate verification step.