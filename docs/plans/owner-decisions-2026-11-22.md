# Owner decisions, 2026-11-22: what was decided, what was done

Record of the decisions the owner asked the orchestrator to make ("decide the best options for me"), with the reason for each and the commit that carries it. Status is as of the last commit. Plans that carry the detail: `resume-source-target-2026-11-22.md`, `provider-copilot-2026-11-22.md` (G-O5), `continue-last-session-2026-11-23.md`.

## The Copilot resume screen (the owner's complaint)

- **Finding.** Copilot's Resume refused before its screen opened, with a status line only. Its cause: 46 of 52 Copilot sessions on this PC point at folders that no longer exist (a probe scratch folder, mostly). Codex refused the same way; Claude opened its screen and checked the folder only at Start.
- **Decision.** All three resume screens open. The folder problem is shown on the screen, and Start (and Full) are blocked while it applies. Previews stay usable where they make no process start in the missing folder. Nothing that starts a process runs without a folder that exists.
- **Done.** `e813334` (the screens and the notice), `7a489c5` (accurate notice text; a Codex summary with no folder refused before any call, because `codex exec` runs in the session's folder; Continue opens the screen for a missing folder on Claude and Codex, and for Copilot refuses only an unlisted session; the facts line says "no folder on record" when there is none).
- **Not refused, on purpose.** A Claude summary with a missing folder: the summary call takes no folder and runs without one, so the folder changes nothing about the process. A Copilot summary runs in its own scratch folder.

## Which Copilot copy runs (G-O5)

- **Finding.** Three installs. The terminal and summary routes run the WinGet 1.0.34 copy; the start route runs the npm shim (1.0.51 at run time). The flags the app passes exist only in the auto-updated 1.0.103 and 1.0.104-3 builds.
- **Decision.** The app does not pick a copy. The setup page marks any copy older than 1.0.103 as a Problem and says what to do. The owner updates or removes the older copy on the machine.
- **Done.** `e64942b`: the setup page reads both copies the app runs (the terminal's and the start's `copilot.ps1`), marks a copy older than 1.0.103 as a Problem with its path, and marks an unreadable version as a Warning, not Ok. The check runs only version reads; no model call.

## Logs: exception messages and paths

- **Finding.** 63 App log lines wrote exception messages, which can carry paths. The rail banner showed the library path. Several lines wrote session titles and ids.
- **Decision.** Logs carry counts, provider names and exception types only, never a message, title, id or path. Screens may show the message (the owner's screen is not a log).
- **Done (App).** `8fc42db`: 82 lines changed; the banner shows the exception type.
- **Done (Core).** `f6fdaf9`: 12 Core files; the callbacks the App wires to app.log now log the type or a count. The crash file's own cleanup log lines are changed too (the earlier exemption covered the crash file's content, not these lines). The crash file's content stays redacted, as designed.
- **Decision for you (not changed).** A run's failure detail is kept in the run's stored alert (`SupervisorRunner`, `SessionRecord.LastAlert`). It can carry an exception's text. It is an owner-facing record, shown on screen, not a log, so I left it. Option: keep the text out of the record too, with the detail shown only on the screen.

## Refused runs

- **Decision.** A refused Claude run also sets a status line, not only the setup page. (A cross-platform start already did.)
- **Done.** `8fc42db`.

## Remember after a refused start

- **Decision.** Keep it: the remembered mode is the owner's own choice, and a refused start doesn't change that. RS-D7 as written.

## The resume-plan defaults (RS-O1 to RS-O4)

- **Decision.** Keep them: last-turns does not cross to another platform; the button reads "Start in <Target>"; Copilot as a target stays off until G-O5 is settled; Claude's title reads "Resume a Claude session".

## Sessions whose folders are gone

- **Finding.** 44 of the 46 are one-prompt test sessions from 19 to 22 May 2026 in the `opentail-net/OpenTail` repository; the repository still exists. Two are empty, from July. None of the six sessions that still have folders has a complete exchange.
- **Decision.** Do not build a recovery path now. Option, if wanted: resume them in their repository root instead of the missing folder.
- **Open.** Who created the May sessions is not known from here.

## Still open, not changed

- A missing-folder Copilot summary runs in the app's scratch folder, so it is not refused; left as is.
- Launch probes for Claude and Codex run before previews without a folder; left as is.
- Continue's scope and the screen check (C7) still need the owner at the PC.
