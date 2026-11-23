# Tabs, panes and zoom: open work

Status: T1 and T2 are built (2026-11-16), tested with counters in place of sessions, and committed in 39eaf53. T3 is not built. What was
not checked on screen is listed under "Not verified". Results: finding F55.

The owner asked for dockable, reorderable tabs, "a bit similar to a browser", and whether that is a real pain to build. It comes
in three sizes. The first is small, the second needs one test first, and the third is cheap only if done a particular way.

Their on-screen checks are in [live-run-checklist.md](live-run-checklist.md), section C7.

## Contracts this must satisfy

Read in the code on 2026-11-16 (`App/SessionWindow.Tabs.cs`, `SessionWindow.xaml`, `SessionWindow.xaml.cs`).

- **The strip is drawn by hand.** `_tabs` is a `List<OpenTab>`; `RefreshTabStrip` clears `TabStrip` (a horizontal `StackPanel`)
  and draws one `Border` per tab in list order, then "+". There is no `TabControl`.
- **`SyncSessionTabs`** runs on every rail refresh. It appends a tab for each recorded session that has none (in name order)
  and removes tabs whose session is gone. It does not reorder existing tabs, so an order the owner sets would survive it.
- **Tab order is not saved.** `_tabs` and `_hiddenSessions` live in memory only.
- **One pane.** `Pane` is one `Grid`. Every view (each terminal, the transcript, the setup, the search) is a child of it, and
  `ShowOnly(element)` shows exactly one and hides the rest. Callers all over the window rely on "one thing is shown".
- **Hidden, not removed.** A terminal is hidden with `Visibility.Hidden`, so it keeps its process, output and input (F45). F45
  also says: "Nothing is moved between parents." Moving a terminal to another parent is not tested.
- **A tab is only a client.** Each session terminal runs `claude attach <short id>`. The session lives in Claude's background
  daemon. `DetachTerminal` removes the terminal and disposes it, and the session keeps running (design principle 2).
- **The tab and the rail agree.** `ActivateTab` selects the rail item, and `NoteRailSelection` opens the tab.
- **Airspace.** The terminal is a native window inside WPF. WPF content cannot be drawn over it (README, Terminal window).

## The three sizes

| Size | What the owner gets | Effort | Risk |
|---|---|---|---|
| T1 Reorder | drag a tab left or right; the order is kept between launches | small | low: no terminal is touched |
| T2 Split | two panes side by side, for example planner left and coder right; drag a tab onto the other pane | medium | one unknown: moving a terminal between two parents in the same window |
| T3 Second window | drag a tab out, or "Open in a new window" | medium if the new window attaches afresh; large if the terminal control itself is moved | whether two `claude attach` clients on one session behave is not known |

A docking library (drop zones, floating panels, as in Visual Studio) is not recommended. It would replace the hand-drawn strip
and `ShowOnly`, and floating panels put a native terminal inside the library's own windows, which is where airspace and
re-parenting problems show. None was tested, so this is a judgement, not a finding.

## Decisions

Recommended by the assistant. The owner has not confirmed them.

- **D-T1** Build T1 now. It is the browser-like part that costs least.
- **D-T2** T2 is the valuable one for the plan-and-code pattern: both sessions in view at once. It starts with a spike.
- **D-T3** T3 is done by attaching afresh in the new window, never by moving the control between windows. Decide it after T2.
- **D-T4** The order and the split are saved beside the state file, with the theme.
- **D-T5** At most two panes, side by side. No grid of four, no vertical split, until two has been used.
- **D-T6 (2026-11-16, while building)** Only session tabs go to the right pane. The setup, the transcript view and the search
  are one view each, so they stay at the left. A search result therefore always opens at the left.
- **D-T7 (2026-11-16, while building)** A paste goes to the terminal that has the native keyboard focus, because WPF's focus
  does not follow these terminals (F55).

## Steps

T1 and T2 are built. Their text is in [Done/tabs-and-panes-done.md](Done/tabs-and-panes-done.md); their on-screen checks are in [live-run-checklist.md](live-run-checklist.md), section C7.

### T3 Second window (parked)

Decided 2026-11-16 (the owner left the choice to the planner): not built. The split covers two sessions side by side, which was
the need. T3 also rests on two `claude attach` clients on one session, which is untested. Taken up again only when the owner wants
a session on a second monitor.

- [ ] Test by hand, with the owner's word: two `claude attach` clients on one session. Record what each shows and whether
      input from both is taken.
- [ ] If that is sound: "Open in a new window" detaches the terminal here and opens a small window that attaches afresh.
      Closing that window brings the tab back to the main window.

## Not verified

- [ ] - The drag on screen, the right-click menu, and a click moving the paste target (see F55).
- [ ] - A real `claude attach` client after a move to a narrower pane: redraw and input.
- [ ] - Two attach clients on one session (T3).
- [ ] - How the terminal control behaves under a `GridSplitter` between two terminals. The rail splitter beside one terminal works (F51).
- [ ] - How the review's Phase 4 item "attach on first select; re-attach when the attach exits" fits with two panes. T2 should come
  after it, or be planned with it.

## Questions for the owner

- [ ] Is T1 then T2 the right order, with T3 only if you still want it after using the split?
- [ ] Two panes side by side: enough, or do you see three sessions at once as the normal case (plan, code, review)?

## Zoom: open points

Zoom is built and checked. These two points stayed open.


- [ ] The palette is Windows Terminal's default, written in. A user with a different Windows Terminal scheme would see the default palette, not theirs. The library's own default colours were not read, because they are not exposed.
- [ ] Only the 16 Campbell colours and the greys are exact. The 256-colour cube follows the xterm values. Claude Code's output was not checked with 256 colours on screen.


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
