# Live run checklist: the one sitting that needs the owner (done items)

> Split from [live-run-checklist.md](../live-run-checklist.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Gates (each needs one real step)
- [x] **`CLAUDE_CONFIG_DIR`**: where does `.claude.json` live when it is set? (round 3) Answered 2026-11-19 with claude 2.1.328: `claude mcp list` with the variable set wrote `.claude.json` inside that folder, so Claude keeps it there. The app now reads it there too (`ClaudePaths`, reliability batch R2). Still to check: one real session under the variable.
