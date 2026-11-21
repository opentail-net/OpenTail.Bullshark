# Setup check: a read-only look at Claude Code's setup (2026-11-16) (done items)

> Split from [setup-check.md](../setup-check.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Steps
- [x] `SetupCheck` in Core (`Core/Health/SetupCheck.cs`): settings, hooks, `~/.claude.json` servers (user and project scope), remote servers, `CLAUDE.md`.
- [x] Tests (`tests/.../Health/SetupCheckTests.cs`, 14): missing files are warnings; broken JSON is a problem; no hooks and no servers are OK; a hook whose program exists is OK; a missing script is a problem; a program found on a given PATH is OK, and a missing one is a problem; a quoted path with spaces is one program; a missing MCP program is a problem; a remote server is listed, not contacted; project servers are checked; `CLAUDE.md` present is OK.
- [x] Dialog (`App/SetupCheckDialog.cs`) and the button (`App/SessionWindow.SetupCheck.cs`, `SessionWindow.xaml`).
