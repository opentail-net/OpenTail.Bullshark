# Button and menu style (2026-11-18, written AFK)

Owner: buttons look like Windows 95/98: square, grey, no icons. Wanted rounded corners, better colours, better highlights.

## What was built
- `src/OpenTail.Bullshark.App/Styles/Controls.xaml`, merged in `App.xaml`: one implicit `Button` style (raised face, 8 px corners, border that
  brightens on hover, sunk on press, accent ring on keyboard focus, accent border for the default button, dimmed when disabled) and keyed
  styles `GoButton` (green launch), `GhostButton` (flat, for the status bar), `AccentButton`, `PillToggle` (not used yet), plus rounded
  `ContextMenu`, `MenuItem` (rounded highlight, tick for checked) and `Separator`.
- Colours: `RailPalette` gained Face, FaceHover, FacePressed, BorderHover, Accent, AccentText, GhostHover, GhostPressed, MenuBg;
  `UiTheme.Apply(palette)` writes them into the application resources and runs whenever a window applies its theme (and in the Setup view and
  the two prompts), so dark and light restyle every button at once.
- The three local button styles (SetupView, ClosePrompt, ResumePrompt) were removed; the prompts keep only their padding and spacing.
- Status-bar buttons are ghost buttons with an icon (Segoe Fluent Icons, falling back to Segoe MDL2 Assets). Refresh, Archive, Restore, Got it,
  Check again, Copy review prompt, Copy as Markdown and Start have an icon too (`UiIcon.Label`). Icons are hints; the text stays.

## Checked
Screenshots (`scripts/Test-NewScreens.ps1 -Exe <net10.0-windows10.0.19041.0 exe>`, dark and light): rail, setup, storage, changes, timeline.
Note: the script's default `-Exe` path is the old `net10.0-windows` folder (stale since the toast package changed the target); pass the new one.

## Not seen on screen (open)
Context menus (row menu, filter menu, tab menu), the Close and Resume prompts, the Idea, Edit and Instructions dialogs, `IsDefault` accent
border, disabled and pressed states, long labels in the status bar on a narrow window. `PillToggle` and `AccentButton` have no user yet.
Menus drew with a hand-written template without submenus: none of the app's menus has one (checked 2026-11-18).
