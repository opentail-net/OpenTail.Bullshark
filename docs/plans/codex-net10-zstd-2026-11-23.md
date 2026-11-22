# Codex history on net10.0: a zstd decoder dependency (plan, 2026-11-23)

Status: **plan; the owner accepted the dependency (2026-11-23: "I don't mind having a dependency, if it gets us over the hill with it").** This revises Codex O1 (`provider-codex-2026-11-21.md`, B4 decisions): Codex history is no longer net11-only.

## Problem

Codex rollouts are zstd-compressed (62 of 63 local files; B1). The BCL's `ZstandardStream` exists in net11.0 only, so on net10.0 the Codex history is empty, the App hides the Codex dropdown (`CodexProvider.Available`), and the Codex tests that page a compressed file assert a refusal on net10.0.

## Decision

- **Dependency:** `ZstdSharp.Port` **0.8.8**, pinned exactly, referenced by `OpenTail.Bullshark.Core` **for net10.0 only** (`Condition` on the target framework). net11.0 keeps the BCL route and gains no package.
- **Licence, checked 2026-11-23:** MIT. The NuGet page (https://www.nuget.org/packages/ZstdSharp.Port) gives MIT and copyright "Oleg Stepanischev 2026"; the repository LICENSE (https://github.com/oleg-st/ZstdSharp/blob/master/LICENSE) gives MIT with "Copyright (c) 2021 Oleg Stepanischev". The year differs between the two; both are MIT, so the notice keeps the repository's wording.
- **API:** the repository README documents a streaming decoder, `DecompressionStream` (read on the repository page, not yet compiled here; stage Z0 verifies it).
- **Obligation:** MIT requires the copyright notice and licence text in redistributions. Any distributed build that contains the package DLL carries a third-party notice (stage Z3). The project's own PolyForm Strict licence is unchanged.

## Stages

**Z0. Verify the package in a scratch project (outside the repository).** **Done 2026-11-23: passed.** Scratch projects in the job folder (`tmp\z0`, outside the repository): a net11.0 project wrote a synthetic 25,490-byte text as a 539-byte stream with `ZstandardStream`; a net10.0 project with `ZstdSharp.Port` 0.8.8 decoded it with `ZstdSharp.DecompressionStream` in memory. Output: 25,490 bytes, matching the plain copy byte for byte (exit 0). The package restores and runs on net10.0. Nothing was written to the repository in Z0.

**Z1. Core decoder seam.** `RolloutReader` (Core/Codex/RolloutReader.cs) opens a rollout through one private method: net11.0 uses `ZstandardStream`; net10.0 uses `DecompressionStream`. Both decode in memory; no temp file, no plaintext copy on disk (R-7, privacy). Plain `.jsonl` rollouts are unchanged. Tests:
- The fixtures that generate compressed rollouts keep their net11.0 path. On net10.0 the fixture is compressed at run time with `ZstdSharp.CompressionStream` (`CodexFixtures.CompressingStream`), so no stored bytes are needed. (The base64-constant route in the first draft of this plan was not used.)
- Every net10.0 test that asserted the compressed refusal now asserts the same result as net11.0 (an **intended behaviour change**, recorded in the commit).
- Gate: Core.Tests counts on both targets do not fall; net10.0 passes the compressed history, paging and find tests that were net11-only.

**Z2. App: Codex on both targets.** `CodexProvider.Available` becomes true on net10.0 too; the dropdown offers Codex on both. The Codex comments that say "net11.0 only" are corrected. Tests: `Available` is true on both targets. Gate: App.Tests counts on both full target names do not fall.

**Z3. Third-party notice. Done 2026-11-23, committed as `91d8ec8`.** A `THIRD-PARTY-NOTICES.md` in the repository root names ZstdSharp.Port 0.8.8, its MIT licence text, and its copyright line as the repository LICENSE gives it. Linked from `README.md` (one line). The project's LICENSE.md is not edited.

**Z1 and Z2 status (2026-11-23): committed as `b530c58`.** Core 2961 (net10.0 and net11.0), App 269 on each full target, all exit 0 when rerun. A damaged compressed rollout threw a decoder-specific exception on each target, and not every reader caught it. **Robustness fix, done 2026-11-23 (uncommitted at the time of writing, committed with this note):** the decoder stream is wrapped in `DecodedStream` (RolloutReader.cs), which translates every decoder failure into `IOException` ("The compressed rollout is damaged or cut short."), and passes cancellation through. Four tests on both targets: a truncated rollout, a garbage rollout with the zstd magic, a usage read with one good and one damaged rollout, and a transcript read that throws IOException. Core 2965 on each target; App 269 on each full target. The limits line (`LimitsView.cs`) now says "decoding compressed rollouts is switched off", because its count only ever covers that case.

**Open items from Z1 to Z4 (recorded, not done):**
- Damaged rollouts are counted as **unreadable** and are not shown in the limits report. Carrying that count into `LimitsReport` is a visible change and needs the owner's decision.
- The "rollouts not read" line of the limits report can no longer appear in production: its count is zero by default, and no production caller turns decoding off. Whether to remove the line is the owner's decision.
- The translation also caught OutOfMemoryException and so reported memory exhaustion as a damaged file. **Fixed 2026-11-23:** OutOfMemoryException and ObjectDisposedException now pass through the translation unchanged (RolloutReader.cs, `IsDecoderFailure`). Not covered by a test: the decoder is internal and a test cannot easily make it run out of memory; the change is reviewed by reading.
- The transcript test accepts any IOException: on net10.0 a truncated read throws `EndOfStreamException`, an IOException subclass, and the wrapper passes IOException subclasses through unchanged.
- Checked against the pre-fix decoder (2026-11-23, the file swapped and restored byte for byte): `ADamagedCompressedRolloutThrowsIOException` and `ADamagedCompressedRolloutIsCountedAndTheGoodOneIsRead` **fail** without the fix, so they test the translation. This was checked on **net11.0** only: on net10.0 the truncated read already throws `EndOfStreamException`, an IOException subclass, so the check there is weaker. The two history-source tests (`ATruncatedCompressedRolloutIsCountedAsUnreadable`, `AGarbageCompressedRolloutWithTheZstdMagicIsCountedAsUnreadable`) **pass** without it: the history reader already catches every exception while listing. They still show that a damaged file does not stop a listing, but they do not test the translation.
- The App's net10.0 run exited 1 once with "Test Collection Cleanup Failure (headless WPF) System.InvalidOperationException" and no failed test. It passed on rerun. It is environmental, and the counts above come from passing runs.

**Z4. Review and commit.** A Sonnet review of Z1 and Z2 (decoder seam, privacy, the net10 test changes). Dispositions recorded here. Local commit only; no push.

## Gates and risks

- **Build collisions:** Z1 runs only after the refactor's launcher stage (R1) is committed, since both touch the Core project and the build artifacts path.
- **Package maintenance:** a single-maintainer port. Pinned exact; the decoder is confined to one private method, so replacing it later is one change.
- **Behaviour change visible to net10.0 users:** Codex history appears where it was hidden. Recorded in the commit and the plan.
- **Test fixtures:** synthetic only; the base64 constant is generated from synthetic JSON lines, never from a real rollout.

## Owner decisions

- Dependency accepted (2026-11-23). Package version and placement (Core, net10.0 only) are the defaults; the owner may move it to a separate assembly later.
