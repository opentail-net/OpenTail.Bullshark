# Review notes: resume-source-target-2026-11-22.md

Reviewer: Sonnet 5.5, plan review only. Checked by reading the plan against SessionWindow.Resume.cs, CodexResume.cs, CopilotResume.cs, ProviderResumeText.cs, ResumeText.cs, CliStartLauncher.cs, Codex and Copilot start launchers, the three prompt records, SessionWindow.xaml.cs (StartRunAsync) and provider-copilot-2026-11-22.md. No build, no tests, no process run.

## Cited lines
Right: Resume.cs :45 :75 :144 :270 :312; CodexResume.cs :60 :142 :222 :258; CopilotResume.cs :40 :137 :241; ProviderResumeText.cs :56; ResumeText.cs :194; SummaryFiles.cs :36; CodexResumeText.cs :37; CopilotResumeText.cs :38.
Wrong: section 5 "Resume.cs:218 Claude's background start": :218 is the Full-resume start (`ResumeSessionId`), inside StartFullResumeAsync. The Summary start uses StartRunAsync (SessionWindow.xaml.cs:995), which writes a supervised run.
Wrong date: the G-O5 measurement is dated 2026-11-23 in provider-copilot :44, not 2026-11-22.

## Table (section 4)
Claude Last-turns and Summary both go through BuildStartTextAsync (:270, :133, :331): true. Last-turns makes no call only because `choice.Level` is null. Codex and Copilot Full are native-id resumes (ResumeCodexAsync, ResumeCopilot with `--resume=<id>`): true. Cells are accurate.

## Must
1. RS-D11 is false for a Claude target. Claude's start puts the text on the command line (`SetupRequest.Task`, fitted by `ResumeText.Fit` to `ClaudeCli.MaxLaunchCharacters`), not a file. Fix: state it as an accepted exception (as provider-copilot R-C3 does for -i) or drop "never on a command line". Also Codex and Copilot pass `$prompt` as an argument to the shim, so the file only protects the encoded line; the long-prompt mitigation in section 8 is not real (no length fit for those two).
2. The plan lists a `Compose` overload only. `ResumeText.Fit` (:150) hard-codes Claude's `Compose` and `Tail` (tool lines, Claude header), so a Codex or Copilot source into Claude needs a Fit overload or a compose delegate. Add to S3.
3. Claude target cannot reuse `StartFromTextAsync`/`BuildStartTextAsync`: both take a Claude `SessionDigest` and Claude `lines`, and rebuild the text. Codex and Copilot send `dialog.PreviewText`. Fix: add a "start from finished text" method for each target (text, folder, model) and say which text is sent (the previewed one).
4. Target needs inputs the three choice records lack: `ResumeChoice` needs Model and Effort, Codex a nullable Model (validated by `CodexModelList.IsValidModelId`), Copilot none. The plan has no target model/effort rule. Fix: default to the target's own saved defaults (`SavedSolo()` for Claude, Codex default model), and say so.
5. Launch checks. The target's check must run before the source's summary call (rule R4/R4d: `ClaudeLaunchCheckAsync`, `CodexLaunchCheckAsync`), or a refused target wastes a model call. Also Claude needs `FolderCheck`/trust (`NotTrusted` refuses at Resume.cs:354); a Codex or Copilot folder may be untrusted by Claude. Codex needs pwsh and `codex.ps1`; Copilot needs pwsh and `copilot.ps1`. RS-D5 names only "missing folder". Add all.
6. RS-D8 default cannot be implemented from what the code knows. `CopilotSetupCheck` reads the version only (`--version`), never the flags; provider-copilot :44 says an older build may or may not reject unknown flags (not verified). The start route runs `copilot.ps1`, a different copy from the `cmd.exe` one. Fix: make the default a plain constant ("Copilot target off, reason: unverified install, G-O5"), with a version threshold only if the owner names one. Do not say "until confirmed".

## Should
1. RS-D2: the owner's rule says "from a summary, not in full". Last-turns is neither; it could be read as allowed ("only not in full"). Reading it as own-platform-only is the conservative one, and the plan marks it RS-O1; fair. But note Summary mode also sends the last N turns word for word (`ProviderResumeText.Compose` tail), so a "summary" cross-platform start carries verbatim turns; say so in RS-D6 or cut the tail for cross-platform.
2. RS-D6/section 5: `Compose` opens "Continues session: <id>" then "continues an earlier <phrase> session". A foreign id in a Claude start, and the Claude header's tail label "(tool calls and results on one line each)" vs "(messages only)", must be decided. Other places naming the platform: tab titles "Codex: resume of ..." (CodexResume.cs:269), "Copilot: resume of" (:273), `StartedLine`, "Resumed ... from" at Resume.cs:368, `CodexResumeRules`/`ProviderResumeRules` wording, the three dialogs' hard-coded cost lines. Add to S2 as a list.
3. Preview must rebuild when the target changes (header differs); the chooser must be in the choice or the builder closure. Say which.
4. RS-D7: Remember is saved after the start in all three flows; with a failed cross start it still saves (Resume.cs:103). Keep, but state it.
5. Claude start is a supervised, recorded, kept-alive run; Codex and Copilot are plain terminal tabs. Say the new session's lifecycle differs by target.
6. Copilot summary needs the single-instance guard (`_supervisor is null` refusal, CopilotResume.cs:160) when Copilot is the source; mention it as a source-side refusal that stays.
7. S4 mixes plan review and code review; S0 says review "at S4". Review now was needed; fix the wording.

## Notes
- S1 27 cases is fine; Gates S1 to S3 are checkable. S2 gate "existing tests change only where they assert title or button" is checkable by diff. S5 is owner-only (fine). S3 gate "dump of Compose output before and after" needs a named test or script, not a prose dump.
- Title change RS-O4 touches ResumePrompt.xaml:4; Codex :4 and Copilot :4 already name the source.

## Privacy
Logs: existing builders log counts only; the plan's new code must keep that (no title, no path; SaveSummary logs only for Codex/Copilot counts, Claude Resume.cs:382 logs a path with the title: existing, not changed by this plan). Summary files are unchanged (RS-D10).

## Not checked
Whether `ResumePrompt` tests assert the old title text; how a real copilot or codex build treats long `$prompt` arguments; any running process.
