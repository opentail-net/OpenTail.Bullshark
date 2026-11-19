# UI polish batch (2026-11-19, written AFK)

Source: brainstorm round 1, UI audit (researcher-reported static read of the XAML/code; the orchestrator also saw the white checkbox, the raw `C--Git` folder name and the wrapping rows on real data).
**Queued** after `night-batch-2026-11-19.md` is reviewed and committed (shared files: `Styles/Controls.xaml`, `SettingsView.cs`, `PillTabs.cs`).
Owner standing wishes: clear minimalism; rounded, consistent; no Windows 95 look; zoom stays in the status bar corner.

## Chosen (small and visible first)
P1 checkbox, P2 text box / list box / slider, P3 scroll bar and tooltip app-wide, P4 combo box everywhere, P5 focus order and pill tab stops, P6 Resume button green, P7 contrast, P8 one-line tooltips without internal IDs, P9 real folder names.
**Not now (recorded for later rounds or the owner):** button-based tab strip with 16 px close targets (M), clickable TextBlocks to ghost buttons + AutomationProperties (M), colour tokens for the hard-coded colours (M), type/radius/padding tokens and one mono font resource (M), one icon set (M),
and the five bigger UX ideas (transcript one-line header with a Details disclosure; Setup with Task, Folder, Start and the rest behind a disclosure; one-line rail rows with a single badge; **status bar with Settings, Insights and zoom in one menu: conflicts with the owner's rule that zoom stays in the corner, so not without their word**; inline confirm bars instead of MessageBox).

## Contracts (read in the code 2026-11-19)
- `Styles/Controls.xaml` is merged in `App.xaml`; brushes `Ui*` are set by `UiTheme.Apply` from `RailPalette` (`Face`, `FaceHover`, `Border`, `BorderHover`, `Accent`, `Warn`, ...). New styles use only those (add a palette member and an `UiTheme` line for a new brush; both themes).
  `SwitchToggle`, `PillToggle`, `UiComboBox`, `GhostButton`, `GoButton` exist. A **local** implicit style in a window/view overrides the app one: `SetupView.xaml` (TextBox, ComboBox template `SetupComboTemplate`, RadioButton `DetailTab`), `ResumePrompt.xaml`
  (a local `GoButton` that shadows the app's: Resume is grey instead of green; `RailPalette.Go` is the intended green), `SessionWindow.xaml` (a window-level `ScrollBar` style using `ScrollThumb` resources set in `ApplyTheme`).
- Checkboxes in use: `ChangesView` (viewed ticks), `StorageView` (archive picks), `SetupView` ("Start when the usage limit resets"). Their `Content`, `IsChecked`, events and tags must keep working.
- Tooltips: texts are set in XAML and code; a few are pinned by tests (grep `ToolTip` strings in `tests/` before editing any); `SearchTip`, `NoteTip`, `MagicTip`, `FactsTip` build row tooltips (keep their content, shorten only the paragraph-style ones the audit names).
- Rail folder headers: `HistoryFolderItem` shows `liveNames[folder.Key]` or the raw key (`ProjectFolder` = the encoded directory under `~/.claude/projects`, e.g. `C--Git-split-OpenTail-Bullshark`; the encoding is lossy: `-` stands for `\`, `:`, `.` and real dashes). `SessionDigest` has no working folder;
  `TranscriptSummary.LatestCwd` has it (read from the transcript's `cwd`). `IndexCache` (Core/Activity) carries digest fields between runs (round-11 rule: a new digest field must be carried by the cache and its reader-build id changes with the code, so an old cache is dropped). `FolderKey`
  (Core) normalises paths; the group key must stay the encoded name (`ShownHistoryFolders()` groups by `ProjectFolder`; tags, the magic rename "visible list" and expanded state use it).
- Contrast: `RailPalette.Warn` (#9A6B00 light / #E0B341 dark, used by Changes flags), muted text, the control edge `UiBorder`. Compute ratios with a script or a unit test (WCAG: 4.5:1 small text, 3:1 UI components), do not eyeball.
- Zoom, Go to…, Insights, Settings stay as they are in the bar.

## Steps (App-heavy; Core only for P9)
- [ ] P1. `CheckBox` implicit style in `Controls.xaml`: a 16 px rounded box (radius 4), `UiBorder` edge, accent fill and a check mark when checked, hover edge, focus ring like the buttons, disabled dimmed, content text in `UiText`. Remove nothing from the views.
- [ ] P2. Implicit `TextBox` (rounded 6, `UiFace`/`UiBorder`, accent edge on focus, caret and selection colours from the palette), `ListBox`/`ListBoxItem` (no system colours: hover and selected from `UiFaceHover`/`UiAccentSoft`), `Slider` (thin rounded track, round thumb; the status-bar zoom slider keeps its width, ticks and snap). Local styles in SetupView/Resume stay only where they differ on purpose; remove the ones that now duplicate.
- [ ] P3. Move the `ScrollBar` style (and its `ScrollThumb*` brushes, set from `ApplyTheme` today) and a themed `ToolTip` style (rounded, `UiMenuBg`, `UiBorder`, `UiText`, a max width of about 420 and wrapping) to `Controls.xaml`/`UiTheme`, so dialogs, the palette and every tooltip use them.
- [ ] P4. Make the themed combo box the implicit `ComboBox` style (merge `UiComboBox` and `SetupComboTemplate` into one template with hover and a focus ring) and drop the Setup-local one and any now-redundant explicit `Style=` uses (`LibraryEditDialog` colour box included).
- [ ] P5. Keyboard: in `SetupView.xaml` the footer is declared before the Task box so Tab reaches Start first: reorder so the visual order is the tab order (or set `KeyboardNavigation.TabIndex`); `PillTabs` makes only the selected pill a tab stop (Left/Right already move).
- [ ] P6. `ResumePrompt.xaml`: delete the local `GoButton` so Resume is the app's green (keep its padding/margin setters in a small local style based on the app one, as `ClosePrompt` does).
- [ ] P7. Contrast: compute the ratios of `Warn`, muted text and `UiBorder` on every surface in both themes (a throwaway script or a Core unit test over the hex values, kept as a test if it is clean: `PaletteContrastTests`); darken light `Warn` to reach 4.5:1 on the light backgrounds; raise the dark control edge modestly toward 3:1 against the panel without making the app loud (decide with a screenshot); record the table in the plan.
- [ ] P8. Tooltips: shorten the paragraph tooltips the audit names (`SetupView.xaml` lines ~184, 301, 310, 339; `SessionWindow.Rail.cs` ~607) to one or two lines and remove internal IDs from user-visible text ("(S9c)", "(F44)", "(D-S10)" and similar: grep user-visible strings for `\((?:[A-Z]-?\w+\d+\w*)\)` patterns). Keep facts; move the long explanation to the README if it is worth keeping. Update any test that pins a changed string.
- [ ] P9. Real folder names. Core: carry the transcript's working folder in `SessionDigest` (`Cwd`, the last `cwd` seen; null when none), through `TranscriptIndex.DigestOf`, `IndexCache` (field + restore) and the tests that build digests (default null). Core helper `FolderDisplay.Of(string projectFolderKey, IEnumerable<string?> cwds)`: the leaf of the most common cwd of the group (case-insensitive), the full path for a tooltip, and the old key when no cwd is known; tests incl. ties, trailing slashes, a drive root ("C:\" -> "C:\"), a UNC path, null/empty. App: `HistoryFolderItem` shows the display name (full path in the tooltip), the group key and all grouping stay on the encoded key. The Storage page project rows use the same helper (they show raw keys too).
- [ ] P10. Screens: extend `scripts/Test-NewScreens.ps1` only as needed (a dialog or two cannot be opened by flags: say which were not seen). Run it; view every picture in dark and light; check the checkbox on Changes, the slider, the scroll bar in the Away page, the folder header, Setup tab order by reading the XAML. Docs: README line, `live-run-checklist.md` items (checkbox look, tooltip look, folder names on real data, Resume is green).

## Checks
Core tests with counts; App build; whole suite; screenshots viewed (RAM and no Bullshark window first). Real-data check like the live run (see `live-run-checklist.md`, "Automated run on the owner's real data"): launch with `--state <scratch> --no-pass` and the real transcripts, `--shot`, view the rail header and the Storage page for P9.

## Orchestrator review
Contracts: group key unchanged by P9; the cache carries `Cwd` and an old cache is not trusted; checkboxes/sliders still bind; nothing loud in dark; light contrast numbers in the plan; no pinned tooltip test broken; Resume green; zoom untouched.

## Open / not decided
- The five bigger UX ideas (owner's taste; the status-bar one contradicts "zoom stays").
- Tab strip, clickable TextBlocks and accessibility names, colour/type tokens, one icon set: later rounds.
