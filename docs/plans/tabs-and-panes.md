# Tabs and panes (2026-11-16)

Status: T1 and T2 are built (2026-11-16), tested with counters in place of sessions, and not committed. T3 is not built. What was
not checked on screen is listed under "Not verified". Results: finding F55.

The owner asked for dockable, reorderable tabs, "a bit similar to a browser", and whether that is a real pain to build. It comes
in three sizes. The first is small, the second needs one test first, and the third is cheap only if done a particular way.

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

### T1 Reorder

- [x] Drag on a tab: after the pointer moves more than a few pixels, the tab changes places with a tab when the pointer passes
      that tab's middle. A click without a drag still activates the tab. "+" stays last. (`SessionWindow.TabDrag.cs`)
- [x] `TabOrder.Move`, `Arrange` and `DropPlace` in Core (`Core/Tabs/TabOrder.cs`), with 16 tests, among them `ATabMovesLeft`,
      `ATabMovesRight`, `AMoveOutOfRangeIsIgnored`, `AWideAndANarrowTabDoNotSwapBack`.
- [x] Save the order of session tabs (by session id) in `tabs.json` and restore it at start. Unknown ids are ignored; new
      sessions go last. (The second run of `scripts/Test-Split.ps1` checks the restore.)
- [ ] Checked on screen with a temporary state folder: three tabs, reorder, wait for one rail refresh (the order holds), reopen.
      Not done: the drag was not driven on screen. For the owner.

Check: `dotnet test` passes; the on-screen steps are seen.

### T2 Split (spike first)

- [x] **Spike, the gate (passed, F55):** in the self-test window (`--selftest`, the counter of F45), move a running terminal from one `Grid`
      to another in the same window, and back. Pass: the counter keeps ticking, a key still reaches it, and it redraws at the
      new size. Record the result as a finding. If it fails, T2 keeps each terminal in its first pane and offers "show in the
      other pane" only for a terminal that is closed and attached again.
- [x] Two pane grids with a splitter between them. Built differently from the first plan: only session tabs go to the right
      pane (D-T6), so `ShowOnly(element)` keeps its meaning and no caller changes. A terminal whose parent is the right pane
      is shown there (`ShowInRight`); everything else is shown at the left as before.
- [x] Each pane has its own strip and its own active tab. Selecting a session in the rail shows it in the pane it is in.
- [x] A tab moves by its right-click menu, or by a drag released over the other half of the pane. Closing the last tab of the
      right pane closes the split. Which sessions are at the right is saved in `tabs.json`.
- [x] `scripts/Test-Split.ps1`: two counters as sessions, 13 checks pass, 2 click checks skipped (the window was covered).
- [ ] Checked on screen with real sessions: planner left, coder right, both live; resize the splitter; collapse the rail;
      paste into each pane after a click in it. For the owner.

Check: the spike's finding is written; the on-screen steps are seen; no terminal is restarted by a move.

### T3 Second window (later)

- [ ] Test by hand, with the owner's word: two `claude attach` clients on one session. Record what each shows and whether
      input from both is taken.
- [ ] If that is sound: "Open in a new window" detaches the terminal here and opens a small window that attaches afresh.
      Closing that window brings the tab back to the main window.

## Not verified

- The drag on screen, the right-click menu, and a click moving the paste target (see F55).
- A real `claude attach` client after a move to a narrower pane: redraw and input.
- Two attach clients on one session (T3).
- How the terminal control behaves under a `GridSplitter` between two terminals. The rail splitter beside one terminal works (F51).
- How the review's Phase 4 item "attach on first select; re-attach when the attach exits" fits with two panes. T2 should come
  after it, or be planned with it.

## Questions for the owner

1. Is T1 then T2 the right order, with T3 only if you still want it after using the split?
2. Two panes side by side: enough, or do you see three sessions at once as the normal case (plan, code, review)?
