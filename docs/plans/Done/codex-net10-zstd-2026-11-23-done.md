# Codex history on net10.0: a zstd decoder dependency (done items)

> Split from [codex-net10-zstd-2026-11-23.md](../codex-net10-zstd-2026-11-23.md) on 2026-11-23. These stages were built and committed when they moved; the open stage and items stay in the plan.

## Stages

**Z0. Verify the package in a scratch project (outside the repository).** **Done 2026-11-23: passed.** Scratch projects in the job folder (`tmp\z0`, outside the repository): a net11.0 project wrote a synthetic 25,490-byte text as a 539-byte stream with `ZstandardStream`; a net10.0 project with `ZstdSharp.Port` 0.8.8 decoded it with `ZstdSharp.DecompressionStream` in memory. Output: 25,490 bytes, matching the plain copy byte for byte (exit 0). The package restores and runs on net10.0. Nothing was written to the repository in Z0.

**Z1. Core decoder seam.** `RolloutReader` (Core/Codex/RolloutReader.cs) opens a rollout through one private method: net11.0 uses `ZstandardStream`; net10.0 uses `DecompressionStream`. Both decode in memory; no temp file, no plaintext copy on disk (R-7, privacy). Plain `.jsonl` rollouts are unchanged. Tests:
- The fixtures that generate compressed rollouts keep their net11.0 path. On net10.0 the fixture is compressed at run time with `ZstdSharp.CompressionStream` (`CodexFixtures.CompressingStream`), so no stored bytes are needed. (The base64-constant route in the first draft of this plan was not used.)
- Every net10.0 test that asserted the compressed refusal now asserts the same result as net11.0 (an **intended behaviour change**, recorded in the commit).
- Gate: Core.Tests counts on both targets do not fall; net10.0 passes the compressed history, paging and find tests that were net11-only.

**Z2. App: Codex on both targets.** `CodexProvider.Available` becomes true on net10.0 too; the dropdown offers Codex on both. The Codex comments that say "net11.0 only" are corrected. Tests: `Available` is true on both targets. Gate: App.Tests counts on both full target names do not fall.

**Z3. Third-party notice. Done 2026-11-23, committed as `91d8ec8`.** A `THIRD-PARTY-NOTICES.md` in the repository root names ZstdSharp.Port 0.8.8, its MIT licence text, and its copyright line as the repository LICENSE gives it. Linked from `README.md` (one line). The project's LICENSE.md is not edited.

**Z1 and Z2 status (2026-11-23): committed as `b530c58`.** Core 2961 (net10.0 and net11.0), App 269 on each full target, all exit 0 when rerun. A damaged compressed rollout threw a decoder-specific exception on each target, and not every reader caught it. **Robustness fix, done 2026-11-23 (uncommitted at the time of writing, committed with this note):** the decoder stream is wrapped in `DecodedStream` (RolloutReader.cs), which translates every decoder failure into `IOException` ("The compressed rollout is damaged or cut short."), and passes cancellation through. Four tests on both targets: a truncated rollout, a garbage rollout with the zstd magic, a usage read with one good and one damaged rollout, and a transcript read that throws IOException. Core 2965 on each target; App 269 on each full target. The limits line (`LimitsView.cs`) now says "decoding compressed rollouts is switched off", because its count only ever covers that case.

Fixed from the open list (recorded in the Z1 and Z2 status above):
- The translation also caught OutOfMemoryException and so reported memory exhaustion as a damaged file. **Fixed 2026-11-23:** OutOfMemoryException and ObjectDisposedException now pass through the translation unchanged (RolloutReader.cs, `IsDecoderFailure`). Not covered by a test: the decoder is internal and a test cannot easily make it run out of memory; the change is reviewed by reading.
