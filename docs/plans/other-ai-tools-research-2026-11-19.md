# Other AI terminals and local models: what they expose (research, 2026-11-19)

Source: one Haiku researcher, official docs, READMEs and source fetched (Nov 2026). **Researcher-reported, not re-checked by the orchestrator.** `?` = UNCONFIRMED from a primary source. Nothing was run locally.
Question asked by the owner: what could Bullshark do in future if it also covered other local AI and other AI terminals? See "What this means for Bullshark" at the end.

## Coding-agent CLIs

| Tool | Session files | List / resume | Hooks / events | Usage locally | Licence | Read-only adapter | Supervised start/resume |
|---|---|---|---|---|---|---|---|
| Codex CLI | `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` (JSONL, source-confirmed); `~/.codex/history.jsonl`; no schema promise | `codex resume [id]`, `codex exec resume --last`, `codex fork` | Yes: SessionStart, PreToolUse, PermissionRequest, Stop; stdin JSON has `session_id`, `transcript_path` | ? | Apache-2.0 | M | S-M (PTY `codex resume <id>`) |
| GitHub Copilot CLI | `~/.copilot/session-state/<id>/events.jsonl` (line schema undocumented); `session-store.db`; `COPILOT_HOME` overrides | `--continue` picker, `/resume`; `--resume` ? | Yes: sessionStart/End, preToolUse, notification (`permission_prompt`, `agent_idle`) | Quota footer only | Proprietary | M | M |
| aider | `.aider.chat.history.md`, `.aider.input.history` in the project folder | `--restore-chat-history` only | `--notifications-command` only | ? | Apache-2.0 | S | M |
| opencode | SQLite `opencode.db`; path via `opencode debug paths` | HTTP `/api/session`, event stream `/api/event`, OpenAPI `/docs/openapi.json`; server start ? | SDK `events.subscribe()`; plugin events ? | ? | MIT | M (schema changed in v2, third-party) | S-M via server |
| Goose | SQLite `sessions.db` (1.10+); Windows `%APPDATA%\Block\goose\sessions\` ? | `goose session list`, `--resume [-n name]`, `export` (JSON/MD) | None found | ? | Apache-2.0 | M | S-M |
| Cline CLI | `~/.cline/data/sessions/<id>/`; index `db/sessions.db` | `cline history --json`, `cline --id <id>` | `PreToolUse` hook (format ?) | ? | Apache-2.0 | S-M | M |
| Qwen Code | `~/.qwen/projects/.../chats/*.jsonl` ? (third-party only) | `--continue`, `--resume <id>`, `--list-sessions`, `/resume` | Lifecycle hooks; config ? | `/goal` token budget | Apache-2.0 | M | M |
| Gemini CLI | `~/.gemini/tmp/<hash>/chats/` (format ?) | `--list-sessions`, `--resume`, `--delete-session` | ? | None documented | Apache-2.0 | M | Skip (see notes) |
| Amp | Threads server-side (`ampcode.com/threads/T-...`); local store ? | `amp threads list / search / continue T-...` | Plugins named, not read | ? | Not stated | L | M |

Notes (researcher): Copilot `-p` mode does not fire prompt hooks. Codex project hooks need trust via `/hooks`; its `notify` fires only on `agent-turn-complete`. Gemini CLI: Google's blog says it stopped for Pro, Ultra and free users on 18 Jun 2026 and Antigravity CLI replaces it (press calls Antigravity closed source; Google's post does not say that). Roo Code CLI: archived 15 May 2026, no resume or list command: skip.

## Local runtimes
- **Ollama** (MIT): `http://localhost:11434`; `POST /api/chat`, `/api/generate`, `/v1/chat/completions`; models `GET /api/tags`, loaded `GET /api/ps`; no session store (the client sends history); no hooks.
- **llama.cpp** `llama-server` (MIT): `/v1/chat/completions` (with `json_schema`), `/v1/models`; the default port differs between docs (9931 vs 8080: confirm with `--help`); no session listing.
- **LM Studio**: `lms server start`, examples use port 1234; `/v1/chat/completions`, `/v1/models`; licence allows internal use, not redistribution.

## Ollama naming call (fits our seam `IMagicNameModel.NameAsync(prompt, ct) -> string?`)
```
POST http://localhost:11434/api/chat
{"model":"<one of GET /api/tags>","stream":false,"think":false,
 "messages":[{"role":"system","content":HaikuNameModel.SystemPrompt},{"role":"user","content":"<prompt>"}],
 "options":{"temperature":0.2}}
-> {"message":{"role":"assistant","content":"<name>\ntags: a, b"},"done":true}
```
Return `message.content` to `MagicName.ParseReply`; `HttpClient.Timeout = HaikuNameModel.CallTimeout` (90 s); `limitInForce = () => false`; a non-200 or timeout throws and `MagicNamer` counts a failure. `format` accepts a JSON schema if strict output is wanted. `num_predict` is unconfirmed.

## Researcher's ranking for supporting other tools
1. **Codex CLI**: Apache-2.0 (the rollout writer is readable source), hooks give `session_id` and `transcript_path`, `codex resume <id>` is interactive (fits the full-terminal rule). Gaps: no documented command lists stored sessions; usage is unknown.
2. **GitHub Copilot CLI**: best Needs-you fit (`permission_prompt`, `agent_idle` notifications, `sessionEnd`, one folder per session). Gaps: `events.jsonl` undocumented, licence proprietary (read what it writes, copy no code). API-first alternative: opencode.

## What this means for Bullshark (orchestrator's reading, 2026-11-19)
- The neutral parts (terminals, rail, palette, toasts, Needs-you, Night, quiet hours, Changes/merge check, tags) need no change; the Claude-specific parts are the session list (`claude agents --json`), transcript JSONL, limits/cache, trust and `claude attach`.
- Tier 4 (local model as a worker) is cheap and already has a seam; tier 0 (generic terminal tab) is UI wiring; tier 1 (provider seam + read-only adapter) is the real design step; tier 2 (supervised start/resume of other tools) only where a tool offers a stable list/state interface.
- Everything above is **an owner decision** before any provider seam work: scope of the product name, per-tool format-drift maintenance, and that principle 3 (never type for the owner) holds for every provider. Design notes: `provider-seam-design-2026-11-19.md`.
