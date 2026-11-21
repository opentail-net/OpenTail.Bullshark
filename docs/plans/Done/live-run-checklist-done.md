# Live run checklist: the one sitting that needs the owner (done items)

> Split from [live-run-checklist.md](../live-run-checklist.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Gates (each needs one real step)
- [x] **`CLAUDE_CONFIG_DIR`**: where does `.claude.json` live when it is set? (round 3) Answered 2026-11-19 with claude 2.1.328: `claude mcp list` with the variable set wrote `.claude.json` inside that folder, so Claude keeps it there. The app now reads it there too (`ClaudePaths`, reliability batch R2). Still to check: one real session under the variable.


## Automated run on the owner's real data, 2026-11-19 (what could be done without a hand on the mouse)

Method: the built App launched with `--state <scratch>` `--no-pass` (no claude call, nothing written to the real state) and the real `~/.claude` transcripts (195 transcripts, 29,678 messages), with
`--open-tab ...` and `--shot`; pictures in `%TEMP%\bullshark-live\shots`. Not a substitute for the clicking items above.
- Limits page / Usage on real data: the page is drawn within 2 s of opening (history read 195 transcripts about 1.2 s); 10 limit hits in 30 days with plausible reset times; the hit table's token column sums all four
  kinds (cache reads dominate: hundreds of millions). OK. Usage per day / projects table is below the hits (the second screenshot taken at 2 s shows the hits; scrolled content not captured).
- Storage page on real data: 88 + 46 + ... transcripts per project, sizes plausible (460 MB, 363 MB), Archive not pressed. OK.
- **Found and fixed:** with no live session in the rail, every rail refresh (the history read finishing) showed the setup page over the Insights/Settings tab (`RestoreSelection` only protected Search, Transcript and Changes). Away on real data was blank
  3 of 3 runs before the fix; after it, Away and Settings stay. (Pre-existing for the old Away/Limits/Storage/Timeline tabs.)
- **Found and fixed:** the ctx meter's ⚠ showed on nearly every row (contexts of 300k to 900k against the 200k default). A session bigger than the chosen window is now judged against 1M (`ContextMeter.EffectiveWindow`).
- Observed, not fixed: the History folder is shown as the encoded name (`C--Git`); rows wrap to three lines in the default rail width; the white band over the status row appeared in the capture taken at 2 s and not at 9 s (so the band is likely a capture-before-render artefact).
- CLI facts: `claude --version` 2.1.328; `claude --help` lists `--permission-mode` choices acceptEdits, auto, bypassPermissions, manual, dontAsk, plan; `--bg`, `--session-id` and `respawn` exist. Whether `--bg` accepts `--permission-mode auto`
  was NOT tested (it would start a real session).
- Not done (needs real sessions or a hand): everything under Phase 3, the tag/auto-naming steps, the toasts, the viewed ticks, the gates (`respawn --all` would restart the owner's real background sessions: not run).
- Owner decisions recorded 2026-11-19: coders run in `auto`; old transcripts are never deleted by the app (archive only), no further work on deletion.

### Found 2026-11-19 (after the transcript batch)
- The `--open-transcript` test hook showed the transcript without making its tab active, so with no live session the rail refresh put the New-session page over it after about 45 s. A real click on a history row sets the tab (`NoteRailSelection`) and was never affected.
  The hook now does the same; checked on the owner's real data: the transcript is still shown at 62 s, in its own tab.
