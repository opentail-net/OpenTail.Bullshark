# Later: claude-scrollback as a bolt-on

Status: evaluated 2026-11-14 (plan H5). Not installed and not used by the product.

## What it is

[claude-scrollback](https://pypi.org/project/claude-scrollback/0.1.0/) (version 0.1.0, alpha) is a pure-Python viewer for Claude Code transcripts. It turns `.jsonl` session files into HTML, serves them on a local web server, and can generate a static site for archiving. Licence: MIT. No runtime dependencies. About 1,400 lines in its generator.

## What it does with usage

It shows a token badge on each message (input, cache read, cache write and output). It does not total usage, and it has no de-duplication. So it cannot answer "how many tokens did this session cost". The OpenTail.Bullshark index does that (F35, F36, F37).

## The risk found

Its `server.py` starts the HTTP server on all network interfaces (`HTTPServer(("", port), ...)`). Transcripts hold your code and your conversations, so running that server exposes them to the local network. It must not be run as it is. If it is used at all, bind it to 127.0.0.1 only, or do not use the server.

## Where it could help later

- Export one session to a standalone HTML file, with `generator.py` copied in (MIT allows it). The file is then opened in a browser, with no server running. This is the one part worth bolting on.
- A static archive of older sessions, for sharing, after the owner decides what may be shared.

## Recommendation

Do not add it now. The History window already gives a read-only view with paging, and the index gives the totals that claude-scrollback lacks. Revisit the single-session HTML export when the owner asks for it, and use only the generator, never the server.
