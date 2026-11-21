# Tabs and panes (2026-11-16) (done items)

> Split from [tabs-and-panes.md](../tabs-and-panes.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Steps
### T1 Reorder
- [x] Drag on a tab: after the pointer moves more than a few pixels, the tab changes places with a tab when the pointer passes
      that tab's middle. A click without a drag still activates the tab. "+" stays last. (`SessionWindow.TabDrag.cs`)
- [x] `TabOrder.Move`, `Arrange` and `DropPlace` in Core (`Core/Tabs/TabOrder.cs`), with 16 tests, among them `ATabMovesLeft`,
      `ATabMovesRight`, `AMoveOutOfRangeIsIgnored`, `AWideAndANarrowTabDoNotSwapBack`.
- [x] Save the order of session tabs (by session id) in `tabs.json` and restore it at start. Unknown ids are ignored; new
      sessions go last. (The second run of `scripts/Test-Split.ps1` checks the restore.)

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
