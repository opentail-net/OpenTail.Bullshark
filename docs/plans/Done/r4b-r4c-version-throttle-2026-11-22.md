# R4b and R4c: version probe throttle and Codex preview order (2026-11-22)

Status: planned and built by the coder on 2026-11-22; boxes ticked as built. Follow-ups of row R4 in [00-current-work.md](../00-current-work.md). Not run on
screen: R4c is checked by reading only.

## Goal

- **R4b.** The supervisor pass asks the launch check before each start and resume (`SupervisorRunner.LaunchRefusal`, set to
  `PassLaunchRefusalAsync`). `ToolVersionProbe` keeps only a Supported verdict, so a version below the minimum or unreadable runs the version
  command about every 15 s while a launch is pending. Keep a non-Supported verdict for `versions.recheckSeconds` (new, default 60; 0 = check every
  time, as now).
- **R4c.** The Codex resume preview makes its summary call (`CodexSummarizer`, one `codex exec`) before the launch check refuses. Run the launch check
  first; a refused launch makes no summary call and the preview says why.

Not in this task: `claude attach` (not verified; stays as it is); the other `FindTranscript` callers; the Claude "from a summary" path, which has the
same order (see Open items).

## Contracts this must satisfy

- `ToolVersionProbe.CheckAsync(minimum)` returns the verdict a fresh check gives, except for a non-Supported verdict kept for the same minimum within the
  window.
- A Supported verdict is kept for the run until the minimum changes (as now).
- A kept non-Supported verdict is used only when: the minimum is the same, the window is above zero, and the time since the reading was taken is zero or
  more and below the window. Otherwise the command runs. A clock that goes backwards therefore re-runs the command (the safe side).
- Checks run one at a time (`_gate`). A check that throws (cancellation included) stores nothing.
- `VersionVerdict`, `VersionGate`, the minimum defaults (Claude 2.1.330, Codex 0.171.0) and the unreadable rule are unchanged.
- The refusal text and the warning text of `LaunchCheckAsync` are unchanged.
- `SupervisorRunner.LaunchRefusal`: called before each Start and Resume; a non-null string refuses the start.
- `BehaviourStore`: an absent, mistyped or non-integer key gives the default and counts as ignored (as the neighbouring readers do). A negative value of
  the new key is read as 0 (see Decisions).
- `CodexResumePrompt`: an empty preview text keeps Start off (`ShowMessage` clears `_previewText`), and the preview shows `CodexPreview.Problem`.

## Existing behaviour map (ToolVersionProbe.CheckAsync)

| Case | Before | After |
|---|---|---|
| Supported, same minimum as the kept one | return the kept reading, no run | unchanged |
| Supported, other minimum | run | unchanged |
| Not Supported (too old or could not be read), same minimum, inside the window | run | return the kept reading, no run |
| Not Supported, same minimum, window passed, or window 0 | run (not kept) | run; kept again with the new time |
| Not Supported, other minimum | run | run |
| The run throws `OperationCanceledException` | propagates | propagates; nothing stored |
| The run throws another exception | caught in `ReadAsync` as CouldNotDetermine | unchanged; the reading is kept like any other non-Supported one |

## Decisions

- The probe's window is a `Func<TimeSpan>`, read at each check, and not a fixed `TimeSpan`. Reason: the App's `_behaviour` is reloaded with each
  supervisor pass (`NoteBehaviour`), and the probes are field initialisers that run before the first load, so a fixed value would ignore a changed knob.
  It is still an optional constructor parameter, as the decision asked. Null means zero.
- The probe's clock is an optional `Func<DateTimeOffset>`, defaulting to `DateTimeOffset.UtcNow`, so tests do not sleep.
- Negative `versions.recheckSeconds` is read as 0, as the decision text says ("a value below 0 is read as 0"). Note: the neighbouring readers give the
  default for an out-of-range value, not 0, and the decision asked to follow them. The two statements conflict; this build takes the explicit outcome.
  The owner can switch it by changing one line in `BehaviourStore.Read`.
- The probe's default with no window passed is zero, so every existing caller and test keeps its behaviour.
- R4c: the launch check runs at the top of `BuildCodexStartTextAsync`, before the messages are read. A refusal returns `new CodexPreview(string.Empty,
  null, refusal)`: no summary call, no read of the messages, and the refusal text in the preview area. Start stays off. The launch check in
  `StartCodexFromTextAsync` stays, because the version can change between the preview and the start.

## Checklist (why next to each box)

- [x] 1. Core `ToolVersionProbe`: keep a non-Supported reading for the window, same minimum only. Why: the pass asks every ~15 s, and a bad version
  gives the same answer within a minute.
- [x] 2. Core `ToolVersionProbe`: optional `recheckWindow` (Func) and `clock` (Func) on the constructor, optional `recheckWindow` on the two factories.
  Why: existing callers and tests compile and keep the run-each-time behaviour.
- [x] 3. Core `BehaviourSettings.VersionsSettings`: `RecheckSeconds` (60) and `RecheckWindow`. Why: the knob sits with the other R4 settings.
- [x] 4. Core `BehaviourStore.Read`: `versions.recheckSeconds`, integer, negative read as 0, text gives the default. Why: the same reader and key layout
  as `versions.unreadable`.
- [x] 5. App `SessionWindow.Versions.cs` and `SessionWindow.xaml.cs` (constructor): both probes get `() => _behaviour.Versions.RecheckWindow`, assigned in the
  constructor, not as field initialisers (a field initialiser cannot read `_behaviour`: CS0236). Why: the knob can change at a pass reload.
- [x] 6. App `SessionWindow.CodexResume.cs` `BuildCodexStartTextAsync`: launch check first; a refusal returns the refusal as the preview. Why: R4c.
- [x] 7. Tests `ToolVersionProbeTests`: refused reading not run again inside the window; run again after it; a changed minimum runs at once; window 0
  runs each time; Supported kept as now; two calls at once run the command once; an unreadable reading is kept too. Why: the four cases of the
  decision plus the unreadable case.
- [x] 8. Tests `BehaviourStoreTests`: default 60 in `AssertDefaults`; a value read; a negative value read as 0; a text value gives the default. Why:
  the defaults and bounds rule of each knob are tested.
- [x] 9. Build and test: Core net11.0 (2719 passed) and net10.0 (2705 passed), App net11.0 and net10.0 windows targets (145 passed each), all with
  `--filter "Category!=Live"`, the artifacts path and `-nodeReuse:false`; exit 0 on each. Why: the owner's rule for a build. The first App build
  failed (CS0236: a field initialiser cannot read `_behaviour`); the probes moved into the constructor (box 5), and the rerun passed.

## Open items (not fixed here)

- The Claude "from a summary" resume has the same order. `SessionWindow.Resume.cs` `PreviewAsync` (lines 104 to 111) and `StartFromTextAsync` (line 287,
  called from line 97) call `BuildStartTextAsync` (line 245), which makes the summary call (line 259). The launch check comes only later, in
  `StartRunAsync` (`SessionWindow.xaml.cs` line 983, called from `SessionWindow.Resume.cs` line 331). A refused launch therefore still makes the summary
  call, and the preview makes it with no check at all. Outside R4c (which names the Codex preview); needs an
  owner decision on the same change.
