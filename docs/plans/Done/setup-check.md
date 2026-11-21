# Setup check: a read-only look at Claude Code's setup (2026-11-16)

> Record. Restructured 2026-11-21: the open items of this plan are carried into the live files in start at [00-current-work.md](../00-current-work.md). The finished items are in the section at the end.

Status (2026-11-16): built, uncommitted. Core tests pass (14 new). Not yet seen on screen.

## Goal

Show, in one place, whether Claude Code's own setup on this computer will work: the settings file, the hooks, the MCP servers, and the standing instructions. It came out of a look at Overclaud (overclaud.dev), which sells a setup bundle. Its useful part is a health check, and the check here is much smaller: it reads, and it changes nothing.

## Decisions

- **D-C1 Read only.** It reads `settings.json`, `~/.claude.json` and `CLAUDE.md`. It writes nothing, starts no hook or server, and never contacts a remote MCP server.
- **D-C2 What a program check means.** For each hook command and each local MCP server, it checks that the program exists (a path, or a name on PATH with a Windows program extension). It also checks that any absolute script path among the arguments exists. It does not check that the program runs or that it does what it says.
- **D-C3 Where it shows.** A "Setup check" button in the status bar opens a dialog. Nothing new is added to the rail or the tabs.
- **D-C4 Three states.** OK, to look at (a warning: works, but something is missing), and problem (will break a session).

## Vendor facts used (checked on this machine, 2026-11-16)

- `~/.claude/settings.json` holds the settings keys (`permissions`, `model`, `enabledPlugins` and others). This machine has no `hooks` key, so the check reports no hooks.
- `~/.claude.json` holds `mcpServers` at the top level (two servers here) and per project under `projects`.
- `~/.claude/CLAUDE.md` is present here.
- A hook is `hooks` → event → groups → `hooks` → `{ type, command }`. An MCP server is `{ command, args }` (local) or `{ url }` (remote).

These shapes are read from the files on this machine, not from a documented schema. The check treats anything it does not recognise as a problem to look at, not as an error that stops it.

## Contracts this must satisfy

- `SetupCheck.Run(claudeDir, claudeJsonPath, pathVariable)` returns the items in order. It never throws on a bad file: a file that cannot be read is an item with state problem.
- The dialog takes a function that runs the check, so it does not know the paths. "Check again" calls it again.
- The button sits in the status bar, to the left of the zoom control.

## Steps

- [ ] Seen on screen: the dialog's layout, the colours in both themes, and the button's place in the bar.

## Not checked, on purpose

- Project `.mcp.json` files: they live in each project's folder and are not read. Reading them means choosing which projects to look at. Not built.
- A project's `CLAUDE.md` and the project-level settings files: not read.
- Whether a hook or a server works: the check only looks for the files. Running them is out of scope for a read-only check.
- Whether a command name on PATH resolves in the same way the Claude Code process resolves it. The check uses the app's PATH.
- The arguments of a hook command that are not absolute paths: a relative script path is not checked, because there is no fixed folder to check it against.

## Done items (from the split of 2026-11-21)

## Steps
- [x] `SetupCheck` in Core (`Core/Health/SetupCheck.cs`): settings, hooks, `~/.claude.json` servers (user and project scope), remote servers, `CLAUDE.md`.
- [x] Tests (`tests/.../Health/SetupCheckTests.cs`, 14): missing files are warnings; broken JSON is a problem; no hooks and no servers are OK; a hook whose program exists is OK; a missing script is a problem; a program found on a given PATH is OK, and a missing one is a problem; a quoted path with spaces is one program; a missing MCP program is a problem; a remote server is listed, not contacted; project servers are checked; `CLAUDE.md` present is OK.
- [x] Dialog (`App/SetupCheckDialog.cs`) and the button (`App/SessionWindow.SetupCheck.cs`, `SessionWindow.xaml`).
