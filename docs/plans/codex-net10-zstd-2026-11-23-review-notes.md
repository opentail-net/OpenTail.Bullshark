# Review notes: Codex on net10.0 zstd (b530c58, 206541d)

Reviewer: Sonnet. Read-only review; nothing built or run. ZstdSharp.dll (net9.0 lib, 0.8.8) searched for exception type names by byte grep only.

## Must fix
None.

## Checked
1. Privacy: no temp file, no plaintext copy. The only text added in these commits: the IOException message "The compressed rollout is damaged or cut short." (constant, wraps the decoder exception as inner) and the usage log line "codex usage: N compressed rollout(s) not read by this build" (count only). No AppLog call added; no decoded text in any message or file name. The decoded stream feeds a StreamReader only.
2. Decoder seam: net11.0 ZstandardStream, net10.0 DecompressionStream(leaveOpen:false); both wrapped in DecodedStream. Read(byte[]), Read(Span) and ReadAsync(Memory) translate; the byte[] ReadAsync, ReadByte and CopyTo overloads route through those in the base Stream. IOException (incl. EndOfStreamException) and OperationCanceledException pass through; Dispose disposes the decoder; Seek/Length/Position/SetLength/Write throw NotSupportedException. Package exception names found: ZstdException, EndOfStreamException, ArgumentException family, InvalidOperationException, NotSupportedException, ObjectDisposedException, OperationCanceledException. ZstdException is not an IOException and is translated; the filter is a blacklist, so no decoder type escapes.
3. Package reference: Core csproj, ItemGroup conditioned on TargetFramework == net10.0, version 0.8.8. net11.0 gains none.
4. Claude: no Claude file in either commit (CliRunner, ResumeText, SummaryFiles, usage code untouched). LimitHistory.cs: one doc-comment line only.
5. Tests: the three named net10.0 expectations (ACompressedRolloutIsRead, APlainRolloutIsListed, ACompressedRolloutIsPaged) now assert the net11.0 behaviour; no assertion weakened; net11-only classes now run on both targets. New usage test (garbage frame) fails without the fix on both targets as stated.
6. Limits wording: "decoding compressed rollouts is switched off" matches the count (only ReadFolder's canDecodeCompressed=false path increments Skipped; default is now true, no production caller passes false).
7. Open items: accurate, except as below.

## Should fix
- RolloutReader.cs line ~173 (IsDecoderFailure): also translates OutOfMemoryException, ObjectDisposedException etc. to "damaged". Fix: exclude OutOfMemoryException (and optionally ObjectDisposedException) from the translation.
- LimitHistory.cs line 78: doc still says "rollouts this build could not decode"; fix to the "decoding switched off" wording used at line 22.
- Plan Z1: says the net10.0 fixture bytes are a stored base64 constant; the code instead compresses with ZstdSharp.CompressionStream (CodexFixtures.CompressingStream). Fix: correct the plan text.
- Plan open item on "fails without the fix": ADamagedCompressedRolloutThrowsIOException uses ThrowsAnyAsync<IOException>; on net10.0 the truncated case already throws EndOfStreamException (an IOException), so there it cannot fail pre-fix. State which target the check was made on; it tests the translation on net11.0 only.
- The Limits line is now unreachable in production (Skipped is always 0). Record that in the plan so the owner can decide to remove it.

## Notes: 3
- DecodedStream.Flush forwards untranslated (harmless).
- Z3 (THIRD-PARTY-NOTICES.md) not yet done; plan records it as a stage, so a build shipping the DLL is not release-ready.
- Plan counts (2965, 269) not re-verified.

## Could not check
Test counts and pass state; behaviour of ZstdSharp when decoding (types listed by byte grep, not reflection); App net10.0 headless-WPF cleanup failure.
