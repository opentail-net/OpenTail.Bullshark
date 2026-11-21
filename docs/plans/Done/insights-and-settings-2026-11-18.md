# Insights and Settings pages, a quieter status bar (2026-11-18)

Owner's words: several screens are thin on their own; better one screen for stats and one for settings, with tabbed sub-controls; the front UI is busy with
icons and clear minimalism is better. Agreed structure (owner: "I like it").

**Queued.** Start only after `tags-and-auto-magic-2026-11-18.md` is reviewed, tested and committed (same files: rail, palette, MagicRename, xaml.cs).

## Target
- **Status bar** (left to right): Needs-you count, status text; right side: `Go to…`, `Insights`, `Settings`. `Retry failed starts`, `Respawn all…` and `Restart trust step`
  stay, shown only when needed (as now). The wrench (setup check), the cog (instructions) and the zoom slider leave the bar. Text only on the three permanent buttons, in the
  quiet `GhostButton` style, **no icons** (icons stay only where they carry meaning: the rail's 🔥🌡️❄️🚀 and the wand are kept as they are; the Go-to/Retry/Respawn/Trust
  icons added on 2026-11-18 are removed from the bar). The second row (limit in force, status text) is unchanged.
- **Insights** (one tab, key `insights`, pill tabs on top): **Away** · **Usage** (the Limits page: usage per day and project, and the limit hits; tab label "Usage") · **Timeline** · **Storage**.
- **Settings** (one tab, key `settings`, pill tabs on top):
  - **General**: theme (Dark / Light, same effect as the rail's toggle, which stays), zoom (slider and percentage, moved from the bar; Ctrl+plus/minus/0 unchanged),
    notifications (sound and flash; toasts) as switches.
  - **Naming**: automatic magic naming (switch), how often an unanswered wait re-alerts (Off / 5 / 15 / 30 / 60 minutes; ui.json `escalateMinutes`, default 15).
  - **Claude**: buttons "Edit standing instructions…" and "Check setup…" (they open the existing dialogs; nothing else changes in them).

## Contracts this must satisfy (read in the code 2026-11-18)
- Pages today: `TabKind` Away, Limits, Storage, Timeline, each its own tab with a view added to `Pane` (`SessionWindow.xaml.cs` `ShowOnly` toggles each view's Visibility by
  reference; `SessionWindow.Tabs.cs` `ActivateTab` switch refreshes the page on show: `RefreshStorageAsync`, `RefreshTimelineAsync`, `RefreshLimitsAsync`, `RefreshAwayAsync(onReturn:false)`).
  The views (`AwayView`, `LimitsView`, `TimelineView`, `StorageView`) keep their code and events (`RefreshRequested`, Got it, archive/restore); they only move into a host.
- **Away on return**: `Activated` runs `RefreshAwayAsync(onReturn: true)`; when the digest has news it opens the Away page. It must now open Insights on the Away sub-tab (same
  conditions: gap, `HasNews`). "Got it" (`DismissAway`) writes last-seen and closes the **Insights** tab if it was opened for the digest, else just moves to the next sub-tab (assumption: close, as today).
- Settings keys are unchanged and read by the same code: `notify` (`NotifyKey`), `toast` (`ToastKey`), `magicAuto` (`MagicAutoKey`), `escalateMinutes` (`EscalateKey`), `railTheme`, the zoom store.
  Toggles write through `UiFile.Set` and report what the file holds afterwards (as `ToggleNotify`/`ToggleToasts` do): a file that cannot be written must not show as changed.
  `AttentionList.NotificationsOn`, `AttentionEscalation.IntervalOf` are the readers; do not change them.
- The palette keeps its toggle entries (keyboard route) and keeps "Open Timeline/Storage/Limits/Away" (now deep links: Insights on that sub-tab), and gains "Open Insights", "Open Settings".
  `Ctrl+Shift+P`, `Ctrl+Shift+N` and the zoom keys are untouched.
- `--open-tab` test flag (`SessionWindow.SelfTest.cs` `OpenTestScreen`, `TestScreen` enum, the options parser): existing names `storage`, `timeline`, `setup`, `changes` must still work
  (now opening Insights on that sub-tab); add `insights`, `usage`, `away`, `settings`, `settings-naming`, `settings-claude`. `scripts/Test-NewScreens.ps1` uses them.
- Styles: `Styles/Controls.xaml` has `PillToggle` (unused so far), `GhostButton`, the themed brushes `Ui*` set by `UiTheme.Apply`. New controls use those only (no new colours outside
  `RailPalette`/`UiTheme`). Light and dark must both work.
- Open tabs are in memory only (check `SessionWindow.Tabs.cs` for any persistence of tab kinds; if tabs are restored from a file, old `Limits/Away/Storage/Timeline` kinds must map to Insights).
- Principle 3: nothing here sends anything to a session. Privacy: no new reads of transcripts.

## Steps (one coder; UI, so checked by build + screenshots)
- [x] 1. `Styles/Controls.xaml`: a `SwitchToggle` style for a `CheckBox`/`ToggleButton` (a rounded track and a knob, accent when on, themed, keyboard focus ring) and make `PillToggle`
  final (selected = accent soft fill + accent border; text only). Test that both render in a throwaway window only if easy; otherwise they are checked on the pages.
- [x] 2. `PillTabs` (App, code or small UserControl): a row of `PillToggle` `RadioButton`/`ToggleButton`s from a list of headers, one selected, event `SelectionChanged(int)`, `Select(int)`,
  themed. Keyboard: Left/Right moves, Enter/Space selects.
- [x] 3. `InsightsView` (UserControl): `PillTabs` on top and a content area that hosts the four existing view instances (move them from `Pane.Children` into the host; remove the
  duplicated big page titles ("Storage", "Limits"...) inside the views, keep their notes and tables). `Show(sub)` selects the tab and raises a `SubTabShown` event so the window refreshes that page
  (call the existing `Refresh*Async`). Remembers the last sub-tab in memory.
- [x] 4. `SettingsView` (UserControl) with the three sub-tabs above. Controls read their state when the page is shown and when a toggle changes (re-read from ui.json after a write).
  Theme and zoom call the existing code paths (`ThemeMark_Click` logic factored into a method `SetTheme(RailTheme)`; `ZoomBy`/store). Escalation choice writes `escalateMinutes` (number of minutes; Off = 0).
  Instructions/Check setup buttons call the existing handlers' bodies (factor the dialog opening into methods; keep the handlers if anything else uses them).
- [x] 5. Window wiring: `TabKind.Insights` and `TabKind.Settings` replace `Away/Limits/Storage/Timeline` (keep the keys for any code that reads them, or migrate); `ActivateTab`, `ShowOnly`
  (include the two hosts), `AwayButton_Click`/`LimitsButton_Click`/`StorageButton_Click`/`TimelineButton_Click` become `OpenInsights(sub)`; away-on-return and Got it as in Contracts;
  palette entries; `OpenTestScreen`. Delete the dead code (the old per-page tab kinds, their `ShowOnly` lines, unused click handlers) only after a grep shows no remaining use.
- [x] 6. `SessionWindow.xaml` status bar (zoom stays in the bar, see Open / decided): remove the wrench, the cog and the zoom bar; the buttons `Away/Limits/Storage/Timeline` are replaced by `Insights` and `Settings` (GhostButton, text only);
  `Go to…` and the conditional buttons lose their icons. Keep names/handlers other code uses (`GoToButton`, `RespawnButton`, `RetryButton`, `TrustButton`); fix anything that referenced the removed controls
  (`ZoomBar`, `ZoomSlider`, `ZoomPercentText`, `InstructionsLabel`, `SetupCheckLabel`) by moving that logic to the Settings page.
- [x] 7. Screens (25 of 25 written and viewed; the open items are under Open / decided): extend `scripts/Test-NewScreens.ps1` with insights-away, insights-usage, insights-timeline, insights-storage (dark), settings-general (dark and light), settings-naming (dark),
  settings-claude (dark); keep the existing screens working. Default `-Exe` of that script points at a stale folder: change it to the `net10.0-windows10.0.19041.0` build folder.
  Run it, view every new picture and fix what is wrong (clipped text, wrong colours in light, tabs not aligned, emoji where text was meant).
- [x] 8. Tests: Core has nothing new unless a pure helper appears (e.g. the escalation choices list <-> minutes mapping in Core with tests: Off=0, 5, 15, 30, 60, an unknown stored value shows
  as "Custom (N min)" without being rewritten). Run the whole suite. README: update the line about the status bar/pages. `docs/plans/live-run-checklist.md`: Insights tabs, Settings switches write
  ui.json, Away opens on return into Insights, zoom keys still work with the slider gone from the bar, the three permanent buttons on a narrow window (1366 px).

## Orchestrator review
Diff against Contracts: keys unchanged, Away-on-return path, Got it, nothing orphaned (no view left parented to `Pane` and also to a host), palette deep links, `--open-tab` old names,
zoom keys still handled, dead code removed only when unused, light theme screenshots read. Look at every new screenshot myself.

## Open / decided
- The rail's theme toggle stays (cheap and found at once); Settings also has it. Say if you want it removed from the rail.
- The rail marks (🔥🌡️❄️🚀, the wand) are kept: they carry meaning now (auto naming fires on 🔥 to ❄).
- No "new Away" dot on the Insights button (not planned; Away still opens by itself on return when there is news).
- Storage stays under Insights although it has archive actions (it is mostly "what takes space").
- **Owner, 2026-11-18 (overrides step 6 and the General list of step 4):** zoom stays in the bottom-right corner of the status bar, as at HEAD. Settings has no zoom row. The wrench and the cog still leave the bar: Check setup… and Edit standing instructions… are on Settings > Claude.
- Got it when the owner opened Insights by hand moves to the next sub-tab (assumption, the plan's wording). When a return opened Insights for the digest, Got it closes the tab, as before.
- Theme and zoom keep their existing write path (no read-back). The three switches and the re-alert choice read the file back and show what it holds.
- A custom re-alert value in ui.json shows as one more pill, "Custom (N min)", and is not rewritten (EscalationChoices, tested).
- The old Limits and Away views never showed: ShowOnly did not toggle them. Hosting them in Insights fixes it.
- Timeline's run and session choosers use a themed drop-down style (Styles/Controls.xaml, UiComboBox). Storage has no drop-downs.
- Open: a white band covers the bottom of some screenshots, cutting the second status row (insights-away-dark once, rail-tag-dark once, storage-dark once). Not specific to Insights; it does not reproduce on demand and was not investigated further.
