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

**Z0. Verify the package in a scratch project (outside the repository).** Compile against `ZstdSharp.Port` 0.8.8 on net10.0; decode a synthetic zstd stream produced on net11.0 with `ZstandardStream`; confirm the namespace and that `DecompressionStream` reads it in one pass. Gate: the round trip matches byte for byte. Nothing is written to the repository in Z0.

**Z1. Core decoder seam.** `RolloutReader` (Core/Codex/RolloutReader.cs) opens a rollout through one private method: net11.0 uses `ZstandardStream`; net10.0 uses `DecompressionStream`. Both decode in memory; no temp file, no plaintext copy on disk (R-7, privacy). Plain `.jsonl` rollouts are unchanged. Tests:
- The fixtures that generate compressed rollouts keep their net11.0 path. For net10.0, the compressed fixture bytes are generated once on net11.0 and stored as a synthetic base64 constant in `CodexFixtures.cs`, so net10.0 tests need no compressor.
- Every net10.0 test that asserted the compressed refusal now asserts the same result as net11.0 (an **intended behaviour change**, recorded in the commit).
- Gate: Core.Tests counts on both targets do not fall; net10.0 passes the compressed history, paging and find tests that were net11-only.

**Z2. App: Codex on both targets.** `CodexProvider.Available` becomes true on net10.0 too; the dropdown offers Codex on both. The Codex comments that say "net11.0 only" are corrected. Tests: `Available` is true on both targets. Gate: App.Tests counts on both full target names do not fall.

**Z3. Third-party notice.** A `THIRD-PARTY-NOTICES.md` in the repository root names ZstdSharp.Port 0.8.8, its MIT licence text, and its copyright line as the repository LICENSE gives it. Linked from `README.md` (one line). The project's LICENSE.md is not edited.

**Z4. Review and commit.** A Sonnet review of Z1 and Z2 (decoder seam, privacy, the net10 test changes). Dispositions recorded here. Local commit only; no push.

## Gates and risks

- **Build collisions:** Z1 runs only after the refactor's launcher stage (R1) is committed, since both touch the Core project and the build artifacts path.
- **Package maintenance:** a single-maintainer port. Pinned exact; the decoder is confined to one private method, so replacing it later is one change.
- **Behaviour change visible to net10.0 users:** Codex history appears where it was hidden. Recorded in the commit and the plan.
- **Test fixtures:** synthetic only; the base64 constant is generated from synthetic JSON lines, never from a real rollout.

## Owner decisions

- Dependency accepted (2026-11-23). Package version and placement (Core, net10.0 only) are the defaults; the owner may move it to a separate assembly later.
