# Resume: the source session and the target platform. History

## Version 3, 2026-11-23 (design revised in chat with the owner)

Changed decisions, with the version 2 text they replace:

- **RS-D2 (was):** Cross-platform start only in Summary mode. Last-turns is not a summary, so it resumes into its own platform. *Default, mine; owner item RS-O1.* A Summary-mode start still carries the verbatim last turns, as now.
  **Now:** the last-turns mode is removed. Its text is a Summary at the Zero level with the tail, so it may go to another platform. RS-O1 answered.
- **RS-D6 (was):** The new session's first text names the source. It is the summary, headed with the source platform's phrase and the summary level, then the target's usual start. Same-platform text is unchanged byte for byte. *Default, mine.*
  **Now:** a header, the summary at its level if any, then the tail if ticked. No session line.
- **RS-D9 (was):** No extra model call. A cross-platform summary is one call, made by the source platform's CLI, as now, with its cost line shown before the call. The target's start is a normal start with that text.
  **Now:** at most one call; Zero makes none; the target's CLI writes the summary when the source is out of quota (owner).
- **RS-D17 (was):** When both refusals apply, the mode reason comes first. A cross-platform full or last-turns start into a disabled Copilot target gets "Only a summary can start in another platform…".
  **Now:** moot, since the target chooser is shown only in Summary.
- **RS-O1 (was):** Last-turns into another platform. *Default: no.* Open.
  **Now:** answered, yes, as Summary at the Zero level with the tail.
- **S5 (was):** Each source platform, Full, Last turns and Summary, each enabled target.
  **Now:** Full and Summary at each level, with the tail on and off.

Not changed in version 3, and still open: RS-D8 says `CopilotTargetEnabled = false`; the code sets it to true under PP-D2 (2026-11-23). The plan has not been brought in line with that yet.
