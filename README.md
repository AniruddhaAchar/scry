# scry

A [ClrMD](https://github.com/microsoft/clrmd)-based .NET memory-dump analyzer **for AI agents**. It
returns **structured JSON**, not human-readable SOS text, so an agent can chain analysis steps
(`stat` → list of a type → dump an object → walk roots) without scraping a debugger's console.

> **Status: v0.0.1, milestone M1 (session model + logging).** The session model (`analyze`/`ps`/
> `health`/`stop`/`kill`), auto-spawn, discovery, and file logging are in place. ClrMD dump loading
> and analysis commands land in M1b+. See [the roadmap](CLAUDE.md#milestones).

## How it works

Two binaries:

| Binary  | Lifetime    | Role |
|---------|-------------|------|
| `scryd` | long-lived  | Host daemon. Loads one dump with ClrMD, holds the runtime open, serves commands over gRPC. |
| `scry`  | short-lived | The CLI an agent invokes per command. Spawns or connects to the host for a dump, issues one gRPC call, prints JSON, exits. |

Loading a dump is expensive (seconds, gigabytes of RAM), so the daemon stays warm between
commands. `scry analyze <dump>` spawns the daemon once and prints a **handle**. Subsequent
commands (`health`, `stop`, `kill`) default to the single active session — no dump path needed.

The client finds the right daemon by deriving a deterministic endpoint id from the dump path, and
talks to it over a **named pipe** (Windows) or **Unix domain socket** (Linux/macOS). See
[ADR 0002](docs/adr/0002-grpc-over-uds-and-named-pipes.md).

## Quickstart

Requires the **.NET 10 SDK**.

```bash
dotnet build scry.slnx

# (Dev only: tell scry where to find scryd since they live in separate build dirs)
export SCRYD_PATH=src/Scry.Host/bin/Debug/net10.0/scryd
SCRY=src/Scry.Client/bin/Debug/net10.0/scry

# Establish a session for a dump:
$SCRY analyze /path/to/app.dmp
```

```json
{
  "handle": "scry-8dab0db081f14f3e",
  "dumpPath": "/path/to/app.dmp",
  "pid": 12345,
  "state": "READY",
  "runtimeVersion": ""
}
```

```bash
# List live sessions:
$SCRY ps

# Query health (no --dump needed — defaults to single active session):
$SCRY health

# Stop the session gracefully:
$SCRY stop

# Or force-kill:
$SCRY kill
```

All commands print JSON to stdout. Errors print a JSON `error` object and exit non-zero.

## Session model

- `scry analyze <dump>` establishes a session. Same dump → idempotent (returns existing handle).
  Different dump while another is live → refused; stop it first.
- `scry ps` lists live sessions (handle, dump, pid, startedUtc).
- `scry health [handle]`, `scry stop [handle]`, `scry kill [handle]` accept an optional explicit
  handle or `--dump`/`--handle` options; default to the single active session.
- One session at a time is enforced. Multi-session support can be added later by dropping the
  refusal in `analyze`.

See [ADR 0004](docs/adr/0004-session-model-analyze-handle.md) for the full decision record.

## Logging & config

Both `scry` and `scryd` write per-process log files to `~/.scry/logs/`. Config is optional at
`~/.scry/scry.config.json`:

```json
{
  "logging": {
    "folder": "/custom/log/dir",
    "level": "Information"
  }
}
```

Pass `-v` / `--verbose` to force `Debug` level. `scry analyze -v` forwards `-v` to scryd.
Stdout stays pure JSON — logs go to the file only.

See [ADR 0005](docs/adr/0005-configuration-and-logging.md).

## Development

```bash
dotnet test   scry.slnx --filter Category=Unit     # fast unit tests
dotnet format scry.slnx --verify-no-changes        # formatting gate

# SCRYD_PATH env var: point scry at the scryd binary (useful during development
# when the two binaries are in separate build output directories).
export SCRYD_PATH=/path/to/scryd[.exe]
```

Pre-commit hooks (formatting + unit tests) are managed with
[prek](https://github.com/j178/prek):

```bash
prek install        # one-time
```

Package versions are centralized in `Directory.Packages.props`; `packages.lock.json` files are
committed for reproducible restores.

## License

MIT (see [LICENSE](LICENSE)).
