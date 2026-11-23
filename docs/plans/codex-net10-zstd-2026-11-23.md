# Codex history on net10.0: a zstd decoder dependency (plan, 2026-11-23)

Status: **Z0 to Z3 built and committed; Z4 (review) and the owner decisions are open.** The owner accepted the dependency (2026-11-23: "I don't mind having a dependency, if it gets us over the hill with it"). This revises Codex O1 (`provider-codex-2026-11-21.md`, B4 decisions): Codex history is no longer net11-only.

## Problem

Codex rollouts are zstd-compressed (62 of 63 local files; B1). The BCL's `ZstandardStream` exists in net11.0 only, so on net10.0 the Codex history is empty, the App hides the Codex dropdown (`CodexProvider.Available`), and the Codex tests that page a compressed file assert a refusal on net10.0.

## Decision

- **Dependency:** `ZstdSharp.Port` **0.8.8**, pinned exactly, referenced by `OpenTail.Bullshark.Core` **for net10.0 only** (`Condition` on the target framework). net11.0 keeps the BCL route and gains no package.
- **Licence, checked 2026-11-23:** MIT. The NuGet page (https://www.nuget.org/packages/ZstdSharp.Port) gives MIT and copyright "Oleg Stepanischev 2026"; the repository LICENSE (https://github.com/oleg-st/ZstdSharp/blob/master/LICENSE) gives MIT with "Copyright (c) 2021 Oleg Stepanischev". The year differs between the two; both are MIT, so the notice keeps the repository's wording.
- **API:** the repository README documents a streaming decoder, `DecompressionStream` (read on the repository page, not yet compiled here; stage Z0 verifies it).
- **Obligation:** MIT requires the copyright notice and licence text in redistributions. Any distributed build that contains the package DLL carries a third-party notice (stage Z3). The project's own PolyForm Strict licence is unchanged.

## Stages

Z0 to Z3 are built and committed. Their text is in [Done/codex-net10-zstd-2026-11-23-done.md](Done/codex-net10-zstd-2026-11-23-done.md). The open stage and items are below.

## Checklist (open)

- [ ] **Z4. Review and commit.** A Sonnet review of Z1 and Z2 (decoder seam, privacy, the net10 test changes). Dispositions recorded here. Local commit only; no push.
- [ ] Damaged rollouts are counted as **unreadable** and are not shown in the limits report. Carrying that count into `LimitsReport` is a visible change and needs the owner's decision.
- [ ] The "rollouts not read" line of the limits report can no longer appear in production: its count is zero by default, and no production caller turns decoding off. Whether to remove the line is the owner's decision.
- [x] The transcript test accepts any IOException: closed in `3f94f29` (2026-11-23). The test now accepts only `IOException` or `EndOfStreamException`, which the reader can give; read in the test code today.
- [x] Checked against the pre-fix decoder (2026-11-23, the file swapped and restored byte for byte; recorded as done when written): `ADamagedCompressedRolloutThrowsIOException` and `ADamagedCompressedRolloutIsCountedAndTheGoodOneIsRead` **fail** without the fix, so they test the translation. This was checked on **net11.0** only: on net10.0 the truncated read already throws `EndOfStreamException`, an IOException subclass, so the check there is weaker. The two history-source tests (`ATruncatedCompressedRolloutIsCountedAsUnreadable`, `AGarbageCompressedRolloutWithTheZstdMagicIsCountedAsUnreadable`) **pass** without it: the history reader already catches every exception while listing. They still show that a damaged file does not stop a listing, but they do not test the translation.
- The App's net10.0 run exited 1 once with "Test Collection Cleanup Failure (headless WPF) System.InvalidOperationException" and no failed test. It passed on rerun. It is environmental, and the counts above come from passing runs.

## Gates and risks

- **Build collisions:** Z1 runs only after the refactor's launcher stage (R1) is committed, since both touch the Core project and the build artifacts path.
- **Package maintenance:** a single-maintainer port. Pinned exact; the decoder is confined to one private method, so replacing it later is one change.
- **Behaviour change visible to net10.0 users:** Codex history appears where it was hidden. Recorded in the commit and the plan.
- **Test fixtures:** synthetic only; the base64 constant is generated from synthetic JSON lines, never from a real rollout.

## Owner decisions

- Dependency accepted (2026-11-23). Package version and placement (Core, net10.0 only) are the defaults; the owner may move it to a separate assembly later.
