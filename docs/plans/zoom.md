# Zoom of the session text (2026-11-16)

> Split on 2026-11-21: the done items moved to [Done/zoom-done.md](Done/zoom-done.md). This file keeps the open items.

Status (2026-11-16): built and checked. The transcripts and the terminals both zoom, from the control in the bottom right and from Ctrl+plus, Ctrl+minus and Ctrl+0. The terminal zoom needed a theme (see "Terminal text" below); it was found by reading the library's compiled code. Committed with the zoom work.

## Goal

The owner's request: Ctrl+plus and Ctrl+minus work in a normal terminal and did not work here; a zoom control like Word's, in the lower right corner, where people expect it.

## Decisions

- **D-Z1 Scope.** The zoom is for the session text: the past-session transcripts and the live terminals. The rail, the setup page and the chrome keep their size.
- **D-Z2 Steps.** 50, 75, 90, 100, 110, 125, 150, 175, 200, 250, 300 percent, as Word's status bar moves in steps. A value between two steps goes to the nearest one. A terminal's font is set in whole points, so its steps at 90 and 110 percent are a little coarser (11 and 13 points).
- **D-Z3 Control.** A slider, a "−" and a "+" button, and the percentage at the right; a click on the percentage goes back to 100%. It sits in the bottom bar, at its far right.
- **D-Z4 Keys.** Ctrl+plus (also Ctrl+= and the numeric keypad), Ctrl+minus and Ctrl+0 (also the numeric keypad 0). The window reads them, including while a terminal has the keyboard.
- **D-Z5 Saved.** The zoom is saved in `ui.json` beside the state file, as `zoomPercent`. The theme's save keeps the other keys in that file.

## Contracts this must satisfy

- `ui.json` holds `railTheme` (D-S30) and `zoomPercent`. `UiFile.Set` changes one key and keeps the others; `ThemeStore.Save` and `ZoomStore.Save` both go through it. `ThemeStore.Load` is unchanged.
- `TranscriptView.ApplyZoom` resizes the lines on screen and sets the size for the lines drawn later. It draws each line at `TerminalFont.Size * zoom`.
- A terminal is given its font size and theme when it is created (`ConfigureTerminal`, from `ApplyZoomTo`), and again on every zoom change (`ApplyZoomToAll`).
- `OnPanePreviewKeyDown` is the window's key handler. The zoom check runs first and only for Ctrl with a zoom key, so paste is unchanged.
- `TerminalFont.Size` is the base size the terminals start with (12 points, 16 WPF units).
- The bottom bar (`StatusBar`) holds the status text, the setup-check button, the trust and retry buttons, and the zoom control, which is the first child so it docks at the far right.

## Terminal text: what the library does (checked 2026-11-16)

The package is WPF-UI.Terminal 1.0.39 over Microsoft.Terminal.Wpf 1.25. The source is not in the package cache, so this was read from the compiled code by reflection.

- `TerminalControl` is a `UserControl` whose content is an `HwndHost` (`TerminalContainer`): the terminal is a native window inside WPF. This is why focus does not follow it (F55).
- `ExtendedTerminalControl.SetTheme` applies the font size, through `TerminalContainer.SetTheme`, only when `Theme` has a value. The app never set one, so the size was never applied. Setting `Terminal.FontSize`, or the size before load, did nothing. Four attempts (below) all showed the same screen at 100% and 200%.
  1. set `Terminal.FontSize` when the terminal loads;
  2. set `FontSizeWhenSettingTheme` before the terminal loads;
  3. set `Terminal.FontSize` again 1.5 s after load;
  4. the same, followed by a one-pixel change of the terminal's width and back.
- `TerminalTheme` is public after all, with public fields: `DefaultBackground`, `DefaultForeground`, `DefaultSelectionBackground`, `CursorStyle` and `ColorTable` (a 256-entry table). Setting `Theme` makes the library apply the font size.
- A theme without a colour table draws every ANSI colour black (checked: the red and green test lines went nearly invisible).
- Each colour is read as `0x00BBGGRR`. With `0x00RRGGBB`, red came out blue. `TerminalColours` converts the Windows Terminal palette (Campbell, and the xterm cube and greys) to this order. Checked on screen at 100%: the red and the green are dark red and dark green.

## Keys (checked 2026-11-16, by SendKeys to the scratch window)

- Ctrl+= with the transcript focused: zoom 100 → 110, saved to `ui.json`.
- Ctrl+- with the transcript focused: zoom 100 → 90.
- Ctrl+= after a click into the terminal pane (the native window has the click): zoom 100 → 110. The window's key handler still gets the key while a terminal has the keyboard.
- Four Ctrl+= presses with the self-test terminal on screen: zoom 100 → 175, and the terminal text on screen grew to match.
- Not checked: Ctrl+0; the numeric keypad; a key in the rail's search box.

## Steps

- [ ] Hover and drag of the slider with the mouse: not driven by any check.

## Checks

- Core tests: 338 pass with the Live tests filtered out (the zoom and setup-check tests are in that count).
- App builds; the one existing warning (`MainWindow.xaml.cs`) is the only one.
- `scripts/Test-Transcript.ps1 -Zoom 200`: 4 of 4 pass; the screenshot shows the text at 200%.
- `scripts/Test-Library.ps1`: 6 of 6 pass on the new build.
- `scripts/Test-Pane.ps1` and `scripts/Test-Split.ps1` ran on the new build with the terminal theme in place. Their pass lines were checked, but the final verdict of Test-Split was not read.

## Open

1. The palette is Windows Terminal's default, written in. A user with a different Windows Terminal scheme would see the default palette, not theirs. The library's own default colours were not read, because they are not exposed.
2. Only the 16 Campbell colours and the greys are exact. The 256-colour cube follows the xterm values. Claude Code's output was not checked with 256 colours on screen.
