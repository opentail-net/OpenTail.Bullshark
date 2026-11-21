# Plan: setup, sessions rail and history (done items)

> Split from [setup-and-sessions.md](../setup-and-sessions.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## 7. Checkboxes
- [x] S0 This plan, with contracts and the invented-ideas table.
- [x] S0b Review against the code, 2026-11-15 (section 9). Corrections made: the `TrustCheck` signature, the migration wording, the peer-name and permission-mode contracts, the run store, and the record key. Not yet built.
- [x] S1 Core (built 2026-11-15; 119 Core tests pass; decisions D-S16 to D-S18; findings F41 to F43): patterns as data (`PatternSpec`, `RunPlan`, `RunNames`). Run store (`RunStore`, D-S12). Supervisor records keyed by `Name` (`SupervisorRunner`, `StateStore`). Not built here: the window and `Program.cs` still use their own specs (S5). Tests:
  - the current `state.json` shape loads, and the running pair is matched after the key change;
  - a second coder record does not throw in `StateStore.Load`;
  - the one-coder default keeps its names (D-S13), so a rename test is an orphan test;
  - the three shipped patterns;
  - N coders named uniquely;
  - a solo brief names no peer.
- [x] S2 Core (built 2026-11-15; 130 Core tests pass; F44): folder validation (`FolderCheck`: exists, repository root, trust state, recent folders from `runs.json`) and `FolderKey`: one key for a working directory and its transcript folder name. Test `FolderKey` on the real folders under the projects folder before relying on the encoding. Tests.
- [x] S3 (built 2026-11-15; F45, F46) Window shell: rail (grouped by folder, collapsible groups, live and history sections, history loaded when opened, open question 8) and pane (terminal, transcript or setup). Test: a hidden terminal is kept alive, not disposed, when the pane shows a transcript (acceptance changed by the advisor, 2026-11-15, to a counter command, not a session; see F45). Proved: a hidden terminal keeps its process, the output written while hidden, and its keystrokes. Not verified: a real `claude attach` while hidden (pending the owner, or one later test session). The history items and the Trust step are compiled and read, not run.
- [x] S4 Setup page (built 2026-11-15; 145 Core tests pass; screenshot checked with PrintWindow; the self-test still passes): pattern choice, model and effort per role, brief extra per role (saved per pattern), overtypable folder with validation, Start. One permission mode per run (D-S14). `SetupPlan` (Validate, Build), `PatternStore` (Beside the state file), `SetupView` (the page). Start checks and saves; it starts nothing and writes no run file. Not done here: launch (S5); Browse compiles (`OpenFolderDialog` exists) but was not clicked; a Trust step for a chosen folder is a message only (the step comes in S5). Model and effort pairs are not verified (S9c).
- [x] S5 (built 2026-11-15; 156 Core tests pass with the fake CLI; the window's live launch is NOT run, pending the owner; F48) Launch from setup: Start writes the run to `runs.json` and hands the specs to the supervisor. Replaces `SessionWindow.Specs` and the second spec list in `Program.cs` (lines 54-58, F43). Tests:
  - a plan > code run (one planner, one coder) starts two sessions with unique names;
  - a plan > many coders run starts one planner and N coders with unique names;
  - restarting the window gives the same specs.
- [x] S6 (built 2026-11-15; 175 Core tests pass; F49, F50) Warm and cold labels on running and past sessions (D-S7). Tests on fixtures. The real-transcript check is `OpenTail.Bullshark.Supervisor --cache-report` (F49): the one-hour warm bucket rewrites 0.075% of 25,300 pairs (passes the 0.5% bar); the one-hour cold bucket rewrites 81% of 47 pairs; the five-minute cold limit is not judged (1 pair). Not verified: the labels on screen (see F50).
