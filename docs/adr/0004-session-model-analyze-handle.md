# ADR 0004 — Session model: `analyze` returns a handle, one scryd per session

- **Status:** Accepted
- **Date:** 2026-06-10
- **Supersedes:** the M0 CLI surface where every command took a required `--dump`.

## Context

In M0 every `scry` command required `--dump <path>`. Repeating the dump path on every
invocation is tedious — for a human and for an agent, which must carry and re-supply the path on
each step of a multi-command investigation. The path is also the only thing tying a command to its
warm `scryd` host, so the ergonomics and the lifecycle are really one problem.

We want an explicit, REPL-like session: establish a host once, then issue bare commands that "just
know" which dump they target.

## Decision

Introduce a **session model** built around a handle and a single active host.

### Operations

1. **`scry analyze <dump>`** — establishes a session. It spawns a detached `scryd` for the dump,
   polls `Health` until `READY` (bounded), and prints a **handle**. The handle *is* the dump-derived
   endpoint id (`scry-<16 hex>`, see `ScryEndpoint`) — deterministic, so the same dump always yields
   the same handle. `analyze` for a dump that already has a live session is **idempotent**: it
   returns the existing handle without spawning a second host.

2. **`scry ps`** — lists running `scryd` sessions: handle, dump path, pid, start time, liveness.

3. **`scry stop` / `scry kill`** — stops a session. `stop` is graceful (the `Shutdown` RPC, with a
   force-kill fallback if the host is unresponsive); `kill` force-terminates the process. Both
   accept an explicit handle, or default to the single active session.

4. **All other commands default to the active session.** `health` (and, from M2, the analysis
   verbs) no longer require `--dump`. With no target specified they resolve the **single** live
   session. `--dump <path>` or `--handle <id>` remain available as explicit overrides.

### One scryd per session

There should be exactly one live `scryd` at a time.

- `scry analyze <dumpB>` while a live session for `<dumpA>` exists is **refused** with a warning
  naming the running handle and telling the user to `scry stop` it first. (Same-dump re-analyze is
  the idempotent exception above.)
- Any command that resolves "the active session" and finds **more than one** live host fails with a
  warning listing the handles and asking the user to stop the extras or pass an explicit target.
  This can only happen if a `scryd` was started out-of-band (not via `analyze`).

### Session registry & discovery

`scryd` is the source of truth for "what's running":

- On startup (once the listener is up) `scryd` writes a descriptor file
  `<temp>/scry/sessions/<handle>.json` = `{ handle, dumpPath, pid, startedUtc, scrydVersion }`.
- On graceful shutdown it removes that file.
- A descriptor whose `pid` is no longer a live `scryd` process is **stale**; clients prune stale
  descriptors lazily whenever they list/resolve sessions (covers force-kill / crash).

Discovery and the one-per-session rules live in a shared `ScrySessions` helper in `Scry.Contracts`,
used by both `scryd` (register/unregister) and `scry` (list/resolve/prune).

### Spawning

`scry analyze` starts `scryd` as an independent process (`UseShellExecute=false`,
`CreateNoWindow=true`, no wait) that outlives the short-lived client. The `scryd` binary is located
next to the `scry` binary (they ship together), with a `SCRYD_PATH` environment override for
development, where the two live in separate build output folders.

## Consequences

- Agents run `scry analyze <dump>` once, then issue bare `scry <verb>` commands — no repeated path.
- This pulls M1's "spawn-on-miss + endpoint discovery" forward, but in an **explicit-verb** form
  (`analyze`) rather than implicit-on-first-use. Cleaner and more predictable for an agent.
- The registry is a directory of small JSON files under the temp dir, not a daemon or database;
  liveness is a pid check. Simple, but a reused pid could in theory mask a stale entry — acceptable
  for v0.0.1 (a follow-up can add a name/handshake check).
- `--dump` is no longer required on most commands; it (and `--handle`) become optional overrides.
- Output stays JSON on every command, success and error, per [ADR 0002](0002-grpc-over-uds-and-named-pipes.md).
- Multi-session concurrency (several dumps at once) is deliberately out of scope; the model enforces
  one. If we later want it, `analyze` drops the refusal and commands always take an explicit handle.

## Command summary

| Command | Target resolution | Effect |
|---|---|---|
| `scry analyze <dump>` | the given dump | spawn host (or reuse), print handle |
| `scry ps` | n/a | list live sessions |
| `scry health [--handle h \| --dump p]` | explicit, else active single | print host health |
| `scry stop [h \| --handle h]` | explicit, else active single | graceful shutdown (force fallback) |
| `scry kill [h \| --handle h]` | explicit, else active single | force-terminate |
