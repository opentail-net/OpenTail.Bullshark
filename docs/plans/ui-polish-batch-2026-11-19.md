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
- [x] P1. `CheckBox` implicit style in `Controls.xaml`: a 16 px rounded box (radius 4), `UiBorder` edge, accent fill and a check mark when checked, hover edge, focus ring like the buttons, disabled dimmed, content text in `UiText`. Remove nothing from the views.
- [x] P2. Implicit `TextBox` (rounded 6, `UiFace`/`UiBorder`, accent edge on focus, caret and selection colours from the palette), `ListBox`/`ListBoxItem` (no system colours: hover and selected from `UiFaceHover`/`UiAccentSoft`), `Slider` (thin rounded track, round thumb; the status-bar zoom slider keeps its width, ticks and snap). Local styles in SetupView/Resume stay only where they differ on purpose; remove the ones that now duplicate.
- [x] P3. Move the `ScrollBar` style (and its `ScrollThumb*` brushes, set from `ApplyTheme` today) and a themed `ToolTip` style (rounded, `UiMenuBg`, `UiBorder`, `UiText`, a max width of about 420 and wrapping) to `Controls.xaml`/`UiTheme`, so dialogs, the palette and every tooltip use them.
- [x] P4. Make the themed combo box the implicit `ComboBox` style (merge `UiComboBox` and `SetupComboTemplate` into one template with hover and a focus ring) and drop the Setup-local one and any now-redundant explicit `Style=` uses (`LibraryEditDialog` colour box included).
- [x] P5. Keyboard: in `SetupView.xaml` the footer is declared before the Task box so Tab reaches Start first: reorder so the visual order is the tab order (or set `KeyboardNavigation.TabIndex`); `PillTabs` makes only the selected pill a tab stop (Left/Right already move).
- [x] P6. `ResumePrompt.xaml`: delete the local `GoButton` so Resume is the app's green (keep its padding/margin setters in a small local style based on the app one, as `ClosePrompt` does).
- [x] P7. Contrast: compute the ratios of `Warn`, muted text and `UiBorder` on every surface in both themes (a throwaway script or a Core unit test over the hex values, kept as a test if it is clean: `PaletteContrastTests`); darken light `Warn` to reach 4.5:1 on the light backgrounds; raise the dark control edge modestly toward 3:1 against the panel without making the app loud (decide with a screenshot); record the table in the plan.
- [x] P8. Tooltips: shorten the paragraph tooltips the audit names (`SetupView.xaml` lines ~184, 301, 310, 339; `SessionWindow.Rail.cs` ~607) to one or two lines and remove internal IDs from user-visible text ("(S9c)", "(F44)", "(D-S10)" and similar: grep user-visible strings for `\((?:[A-Z]-?\w+\d+\w*)\)` patterns). Keep facts; move the long explanation to the README if it is worth keeping. Update any test that pins a changed string.
- [x] P9. Real folder names. Core: carry the transcript's working folder in `SessionDigest` (`Cwd`, the last `cwd` seen; null when none), through `TranscriptIndex.DigestOf`, `IndexCache` (field + restore) and the tests that build digests (default null). Core helper `FolderDisplay.Of(string projectFolderKey, IEnumerable<string?> cwds)`: the leaf of the most common cwd of the group (case-insensitive), the full path for a tooltip, and the old key when no cwd is known; tests incl. ties, trailing slashes, a drive root ("C:\" -> "C:\"), a UNC path, null/empty. App: `HistoryFolderItem` shows the display name (full path in the tooltip), the group key and all grouping stay on the encoded key. The Storage page project rows use the same helper (they show raw keys too).
- [x] P10. Screens: extend `scripts/Test-NewScreens.ps1` only as needed (a dialog or two cannot be opened by flags: say which were not seen). Run it; view every picture in dark and light; check the checkbox on Changes, the slider, the scroll bar in the Away page, the folder header, Setup tab order by reading the XAML. Docs: README line, `live-run-checklist.md` items (checkbox look, tooltip look, folder names on real data, Resume is green).

## Checks
Core tests with counts; App build; whole suite; screenshots viewed (RAM and no Bullshark window first). Real-data check like the live run (see `live-run-checklist.md`, "Automated run on the owner's real data"): launch with `--state <scratch> --no-pass` and the real transcripts, `--shot`, view the rail header and the Storage page for P9.

## Orchestrator review
Contracts: group key unchanged by P9; the cache carries `Cwd` and an old cache is not trusted; checkboxes/sliders still bind; nothing loud in dark; light contrast numbers in the plan; no pinned tooltip test broken; Resume green; zoom untouched.

## Open / not decided
- The five bigger UX ideas (owner's taste; the status-bar one contradicts "zoom stays").
- Tab strip, clickable TextBlocks and accessibility names, colour/type tokens, one icon set: later rounds.

### Decisions and numbers (built 2026-11-19, coder)

Contrast was computed (WCAG 2.x relative luminance, a throwaway PowerShell script over the `RailPalette` hex values, not eyeballed). "Min" is the lowest ratio over the surfaces listed. Small text needs 4.5:1; a control's edge or a state needs 3:1.

Dark surfaces: Background #111316, Panel #0C0D10, tab bar #1A1B1E, Face #1C2027, Face hover #262C36, menu #171A20, Selection #2B3A4F.

| Dark item | Colour | Ratios over the surfaces | Min | Result |
|---|---|---|---|---|
| Muted text | #9AA3AE | 7.29 / 7.61 / 6.74 / 6.40 / 5.50 / 6.82 / 4.52 | 4.52 (Selection) | pass, unchanged |
| Warn | #E0B341 | 9.48 / 9.90 / 8.77 / 8.32 / 7.15 / 8.87 / 5.87 | 5.87 | pass, unchanged |
| Control edge before | #2A2F38 | 1.38 / 1.45 / 1.28 / 1.22 / 1.04 / 1.30 / 1.17 | 1.04 | was far from 3:1 |
| Control edge now (UiBorder) | #4A5566 | 2.47 / 2.57 / 2.28 / 2.16 / 1.86 / 2.31 / 1.53 | 1.53 (Selection) | raised; 3:1 not reached (see decision 3) |
| Edge hover now | #5D6779 | 3.26 / 3.41 / 3.02 / 2.86 / 2.46 / 3.06 / 2.02 | 2.02 (Selection) | pass on the background |

Light surfaces: Background #F4F6FA, Panel #E9EEF6, tab bar #E9EEF6, Face #FFFFFF, Face hover #EAF0FA, menu #FFFFFF, Selection #D6E4F7.

| Light item | Colour | Ratios over the surfaces | Min | Result |
|---|---|---|---|---|
| Muted text | #5A6270 | 5.68 / 5.28 / 5.28 / 6.15 / 5.37 / 6.15 / 4.77 | 4.77 (Selection) | pass, unchanged |
| Warn before | #9A6B00 | 4.34 / 4.03 / 4.03 / 4.69 / 4.10 / 4.69 / 3.64 | 3.64 | fail on the panels |
| Warn now | #8A5F00 | 5.22 / 4.85 / 4.85 / 5.65 / 4.93 / 5.65 / 4.38 | 4.38 (Selection, not a text surface) | pass on every surface Warn is drawn on |
| Control edge before | #C7CFDB | 1.45 / 1.35 / 1.35 / 1.57 / 1.37 / 1.57 / 1.22 | 1.22 | a plain light check box was almost invisible |
| Control edge now (UiBorder) | #7F8B9D | 3.19 / 2.96 / 2.96 / 3.45 / 3.02 / 3.45 / 2.68 | 2.68 (Selection) | pass on the background |
| Edge hover now | #5E6B80 | 4.99 / 4.63 / 4.63 / 5.40 / 4.71 / 5.40 / 4.19 | 4.19 (Selection) | pass |

Decisions taken while building (the owner may change any of them):
1. **A palette member `Edge` drives `UiBorder`** (the controls). `Border` still draws the rail's lines and the setup rules, so those did not change.
2. **The light control edge is raised to 3:1 as well.** The brief named only the dark edge; without this a light check box has a 1.45:1 edge. This is outside the brief: review it on the screen.
3. **The dark edge is held at 2.47:1, not 3:1.** A 3:1 grey edge on near black reads loud, and the owner asked for no loud dark theme. The screenshots decide the look; the number is the bound.
4. **The hover edge is raised** (dark #5D6779, light #5E6B80), so the hover still differs from the resting edge now that the resting edge is brighter.
5. **Light Warn darkened** to #8A5F00 (the only colour on the palette that changed for text).
6. **Slider:** horizontal only (the app uses only the zoom), no keyboard focus ring (the zoom also takes Ctrl+plus and Ctrl+minus). Width, tick frequency and snap are unchanged.
7. **Tooltips:** `HasDropShadow` is off so the popup can be transparent and its corners rounded (the context-menu style does the same). `--shot` captures the window, not its popups, so the tooltip look is not in the screenshots: it is for the live checklist.
8. **Local styles removed** where they copied the app's look: the setup page's text boxes, combo boxes and tooltips; the Resume prompt's text box; the dialogs' text box brushes; the rail's search box brushes. Kept on purpose: the setup page's tabs and labels, the Resume prompt's radio buttons and segments, the palette's list rows (its own selection colour), and the Settings switches (`SwitchToggle`).
9. **P9 name rule (changed from the brief during the real-data check):** the leaf of the most common working folder among those that encode to the group's own key (the transcripts' last cwd, case-insensitive; ties to the first in ordinal order). The brief said "the most common cwd of the group"; that let a group named after a sub-folder its sessions had moved to. Real data showed it: group `C--Git-Public` took the name `OpenTail.Stingray` from 1 sub-folder session, duplicating that group's own row. Now a group with no matching folder keeps its key (`C--Git-Public`). The encoding is lossy, so `my-repo` and `my
epo` both count for `C--Git-my-repo`. The group key is unchanged.
10. **Storage names are drawn again when the history is read.** The Storage listing can be drawn before the history is read (the history read starts at launch); the last listing is kept and drawn again with the names once the history is in. Without this the page kept Claude's names until the next Refresh (seen on the real-data check).
11. **Button content alignment:** the base Button template ignored `HorizontalContentAlignment` (the Storage rows set Left and were centred). The content presenter now follows the button's alignment properties; every other button keeps the default (centre), so no other button changes.

Real-data check (2026-11-19, the owner's real transcripts, scratch state, `--no-pass`, the stand-in claude first on PATH; the logged calls were `daemon status`, `agents --json`, `--version` only): the rail's History header reads `Git` for `C--Git` (94 transcripts), the full path is in its tooltip, and the group keeps the key `C--Git`. The Storage rows read `Git`, `OpenTail.Stingray`, `demo-handoff`, `work`; the row `C--Git-Public` keeps its key (its sessions moved to a sub-folder, see decision 9). The `C--Users-...-ovc-live-switch-...` rows show `work`, the leaf of their working folder.

Open after the real-data check: the Usage page's "Top project" column still shows the raw key (`scratch-repo` on the fixture), as the Insights tables do; the brief named only the rail and the Storage rows. A group whose sessions all moved to a sub-folder keeps its raw key on the rail too.

Not seen on screen: the resume prompt, the close prompt, Edit session, Add idea, Instructions and the palette's own list (no flag opens them; `Test-NewScreens.ps1` covers the rest). Their XAML and code were checked by reading and by the build only.
