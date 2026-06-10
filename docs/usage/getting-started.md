# Getting started

This walks through the M0 skeleton: starting a host and talking to it. There is no dump analysis
yet — that arrives in M1 — but the full client ↔ host round trip works today.

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` ≥ 10.0).
- Build the binaries: `dotnet build scry.slnx`. They land as `scry` and `scryd` under each
  project's `bin/<config>/net10.0/`.

## Concepts

- **One host per dump.** A `scryd` process serves exactly one dump. The endpoint it listens on is
  derived from the dump path (`scry-<hash>`), so every `scry` command for that dump reaches the
  same host.
- **Transport.** A named pipe on Windows, a Unix domain socket on Linux/macOS. No TCP port.
- **Output.** Every command prints JSON to stdout. Failures print a JSON `error` object and exit
  non-zero.

## Start a host

```bash
scryd --dump /path/to/app.dmp --idle-timeout 10
```

- `--dump <path>` (required) — in M0 the file isn't opened; the path only seeds the endpoint id.
- `--idle-timeout <minutes>` (default `10`, `0` disables) — the host exits after this long with no
  RPC activity, so abandoned hosts don't linger holding a dump's file lock.

The host logs readiness to **stderr** and keeps running:

```
info: scryd ready on endpoint scry-8dab0db081f14f3e for dump /path/to/app.dmp
info: Now listening on: http://pipe:/scry-8dab0db081f14f3e
```

## Query health

```bash
scry health --dump /path/to/app.dmp
```

```json
{
  "endpoint": "scry-8dab0db081f14f3e",
  "state": "READY",
  "runtimeVersion": "",
  "detail": "M0 skeleton: no runtime loaded"
}
```

`state` is `LOADING`, `READY`, or `FAILED`. From M1, a host stays `LOADING` until the dump and DAC
are resolved, and `runtimeVersion` is populated.

## Shut a host down

```bash
scry shutdown --dump /path/to/app.dmp
```

```json
{ "endpoint": "scry-8dab0db081f14f3e", "shutdown": "requested" }
```

## When no host is running

```bash
scry health --dump /path/to/never-started.dmp
```

```json
{
  "error": {
    "code": "UNAVAILABLE",
    "message": "no scryd host is reachable for this dump (endpoint scry-...)",
    "hint": "start one with: scryd --dump \"/path/to/never-started.dmp\""
  }
}
```

Exit code is `1`. (M1 adds spawn-on-miss, where `scry` starts the host for you.)

## Common options

| Option | Commands | Meaning |
|---|---|---|
| `--dump <path>` | all | Identifies the dump (and thus the host endpoint). Required. |
| `--timeout <seconds>` | `health`, `shutdown` | RPC timeout; `0` means no timeout. Default `10`. |
