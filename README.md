# scry

A [ClrMD](https://github.com/microsoft/clrmd)-based .NET memory-dump analyzer **for AI agents**. It
returns **structured JSON**, not human-readable SOS text, so an agent can chain analysis steps
(`stat` → list of a type → dump an object → walk roots) without scraping a debugger's console.

> **Status: v0.1.0** — first public release; feedback welcome. A single-binary CLI + daemon with:
> the session model (`analyze`/`ps`/`health`/`stop`/`kill`), file logging, ClrMD dump loading,
> managed thread stacks (`stack`), heap analysis (`dumpheap`/`dumpexceptions`/`printexception`),
> object & array inspection (`dumpobject`/`dumparray`), thread, root-path, and concurrency triage
> (`clrthreads`/`gcroot`/`syncblk`/`dumpasync`), an [agent skill](skills/scry/SKILL.md) (how to
> drive scry + a bounded, give-up-aware reasoning loop), and tag-driven distribution
> (global tool + per-RID zips).
> See [the roadmap](CLAUDE.md#milestones).

## How it works

`scry` is a single executable with two modes:

- **CLI mode** — the default. An agent invokes it per command (analyze, health, dumpheap, etc.).
- **Host mode** (`scry __host ...`) — an internal mode that runs the long-lived gRPC host daemon.

When you run `scry analyze <dump>`, the CLI spawns itself in host mode as a detached daemon. The
daemon loads one dump with ClrMD, holds the runtime open, and serves commands over gRPC. Loading
is expensive (seconds, gigabytes of RAM), so the daemon stays warm between commands, and the same
executable instance serves both the CLI and daemon roles.

The daemon is found by deriving a deterministic endpoint id from the dump path and listens over a
**named pipe** (Windows) or **Unix domain socket** (Linux/macOS). See [ADR 0002](docs/adr/0002-grpc-over-uds-and-named-pipes.md)
and [ADR 0007](docs/adr/0007-single-binary-and-distribution.md).

## Install

### Global tool (requires ASP.NET Core 10 runtime)

```bash
dotnet tool install -g Scry.Cli
scry analyze /path/to/app.dmp
```

### Self-contained zips (no runtime required)

Download pre-built per-RID archives from [GitHub Releases](https://github.com/AniruddhaAchar/scry/releases).
Unzip and run the `scry` executable directly.

## Quickstart (development)

Requires the **.NET 10 SDK**.

```bash
dotnet build scry.slnx

# Collect a local dump for testing:
scripts/collect-dump.ps1 -ProcessId <pid>

# Establish a session for a dump (the single exe spawns itself in host mode):
src/Scry.Client/bin/Debug/net10.0/scry analyze /path/to/app.dmp
```

```json
{
  "handle": "scry-8dab0db081f14f3e",
  "dumpPath": "/path/to/app.dmp",
  "pid": 12345,
  "state": "READY",
  "runtimeVersion": "8.0.0"
}
```

```bash
# (Development: define a shorthand for your build)
SCRY=src/Scry.Client/bin/Debug/net10.0/scry

# List live sessions:
$SCRY ps

# Walk managed thread stacks (all threads, as JSON):
$SCRY stack

# Walk a single thread (by OS id):
$SCRY stack --thread 1234

# Managed threads (state flags, GC mode, lock count, current exception):
$SCRY clrthreads

# Concurrency triage:
$SCRY syncblk                           # held monitors + owner/waiters (deadlocks)
$SCRY dumpasync                          # async state machines in flight (async hangs)

# Heap queries (the first heap command warms a one-time in-memory snapshot):
$SCRY dumpheap                          # heap statistics
$SCRY dumpheap --type System.String     # objects of a type, paged
$SCRY dumpexceptions                    # live exceptions
$SCRY printexception --address 0xCAFEBABE # detail for one exception
$SCRY dumpobject --address 0xFACADE     # inspect an object's fields
$SCRY dumparray --address 0xC0FFEE      # walk an array's elements, paged

# Why is an object still alive? Find the GC root paths that retain it:
$SCRY gcroot --address 0xDECADE         # first root path (--max-paths N for more)

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

## Symbols & dumps

**Windows + released runtimes:** The DAC for released .NET runtimes is resolved automatically from
the [Microsoft symbol server](https://msdl.microsoft.com/download/symbols), using ClrMD's
[default symbol-server resolution](https://github.com/microsoft/clrmd/blob/main/doc/GettingStarted.md#getting-the-dac-from-the-symbol-server).
The first `scry analyze` may take a moment as it downloads the DAC, but subsequent commands on the
same session reuse it. Offline or preview runtimes can be served via a `~/.scry/scry.config.json`
symbol path.

**Linux/macOS:** DAC acquisition is less turnkey; scry currently supports local-machine dump
analysis there, and broader cross-platform symbol robustness is deferred to a later milestone. See
[ADR 0008](docs/adr/0008-dac-and-symbol-resolution.md).

## Logging & config

`scry` writes per-process log files to `~/.scry/logs/`. Config is optional at
`~/.scry/scry.config.json`:

```json
{
  "logging": {
    "folder": "/custom/log/dir",
    "level": "Information"
  },
  "symbols": {
    "path": "srv*C:\\sym*https://msdl.microsoft.com/download/symbols"
  }
}
```

The `symbols.path` configures the DAC and binary cache location. Pass `-v` / `--verbose` to force
`Debug` level. `scry analyze -v` forwards `-v` to the host. Stdout stays pure JSON — logs go to
the file only.

See [ADR 0005](docs/adr/0005-configuration-and-logging.md) and [ADR 0008](docs/adr/0008-dac-and-symbol-resolution.md).

## Development

```bash
dotnet test   scry.slnx --filter Category=Unit     # fast unit tests
dotnet format scry.slnx --verify-no-changes        # formatting gate
```

Pre-commit hooks (formatting + unit tests) are managed with
[prek](https://github.com/j178/prek):

```bash
prek install        # one-time
```

Package versions are centralized in `Directory.Packages.props`; `packages.lock.json` files are
committed for reproducible restores. The single `scry` executable self-spawns in host mode
(`scry __host ...`), so no separate binary location setup is needed during development.

## License

MIT (see [LICENSE](LICENSE)).
