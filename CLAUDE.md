# CLAUDE.md

Guidance for Claude Code (and humans) working in this repo.

## What scry is

`scry` is a ClrMD-based .NET memory-dump analyzer built **for AI agents**: it returns
**structured JSON**, not human-readable SOS text. It is a single executable with two modes:

- **CLI mode** — the default. Invoked by agents per command (analyze, health, dumpheap, etc.).
- **Host mode** (`scry __host --dump ...`) — an internal daemon mode. The CLI spawns itself in
  this mode as a long-lived process. It loads one dump with ClrMD, holds the `ClrRuntime` open,
  and serves analysis commands over gRPC. Loading is expensive (seconds, GBs of RAM); keeping it
  warm is the whole reason the daemon exists.

The two modes are the same binary (`scry`), so they stay version-matched automatically. See
[ADR 0007](docs/adr/0007-single-binary-and-distribution.md).

## Layout

```
scry.slnx                    Solution (modern XML format)
Directory.Build.props        Shared MSBuild settings (TFM, nullable, lock files, warnings-as-errors)
Directory.Packages.props     Central Package Management — all package versions live here
global.json                  Pins the .NET SDK band
src/
  Scry.Contracts/            Shared gRPC contract (Protos/scry.proto) + ScryEndpoint naming
  Scry.Host/                 Host mode library (HostMode.RunAsync; Microsoft.NET.Sdk + AspNetCore ref)
  Scry.Client/               Single scry exe (CLI + dispatch to host mode; System.CommandLine)
  Scry.Core/                 Shared config, logging, and path helpers
  Scry.Analysis/             ClrMD integration and analysis engine
tests/
  Scry.UnitTests/            xUnit v3. Every test is [Trait("Category","Unit")].
docs/
  adr/                       Architecture Decision Records
  usage/                     Usage docs
.github/
  workflows/
    release.yml              Tag-triggered release: self-contained zips + NuGet tool publish
```

## Build / test / run

```bash
dotnet build scry.slnx
dotnet test  scry.slnx --filter Category=Unit       # fast, dump-free unit tests
dotnet format scry.slnx --verify-no-changes         # what the pre-commit hook enforces

# End-to-end (M1 session model): spawn a host (via self-exec), query it, stop it.
# The single exe spawns itself in host mode (__host) automatically.
scry analyze [-v] <path/to/dump>   # spawns itself in host mode, waits for READY, prints handle
scry ps                            # list live sessions
scry health                        # health of the single active session
scry stop                          # graceful shutdown (force-kill fallback)
scry kill                          # force-kill

# health/stop/kill accept an explicit handle or --dump to target a specific session:
scry health --handle scry-<hex>
scry health --dump <path>
```

## Conventions

- **Central Package Management:** never put a `Version=` on a `<PackageReference>`. Add/bump
  versions in `Directory.Packages.props` only.
- **Package lock files** (`packages.lock.json`) are committed; restores are reproducible.
- **All unit tests carry `[Trait("Category", "Unit")]`** so the pre-commit hook can run exactly the
  fast set. Integration tests (which need real dumps) are a later concern.
- **Warnings are errors** solution-wide. Generated protobuf code is excluded from style rules via
  `.editorconfig`.
- Pre-commit hooks run `dotnet format --verify-no-changes` and the unit tests; managed by **prek**
  (`.pre-commit-config.yaml`).

## Core constraints (will matter from M1 on)

1. **ClrMD is single-threaded** — the DAC underneath is not thread-safe. All ClrMD access must be
   serialized onto one dedicated analysis thread; gRPC handlers enqueue work and await it. See
   [ADR 0003](docs/adr/0003-single-threaded-analysis-worker.md).
2. **DAC must match the dump's runtime version**, or analysis fails.
3. **Host architecture must match the dump** (x64 dump → x64 host). v0.0.1 scope: x64 + arm64;
   x86 dumps are rejected.
4. **Dumps contain secrets** (connection strings, tokens, PII). Endpoint access is scoped by
   filesystem permissions — see [ADR 0002](docs/adr/0002-grpc-over-uds-and-named-pipes.md).

## Logging & config

`scry` writes a per-process log file to `~/.scry/logs/` (configurable via
`~/.scry/scry.config.json`). The `-v`/`--verbose` flag forces `Debug` level. On `scry analyze -v`,
the `-v` is forwarded to the spawned host. Stdout always stays pure JSON — logs go to the file
only. Symbol path is configurable via `~/.scry/scry.config.json` under `symbols.path` (see ADR 0008).
See [ADR 0005](docs/adr/0005-configuration-and-logging.md).

## Layout additions (M1 session model)

```
src/
  Scry.Core/          Shared config, logging, and path helpers (Scry.Client + Scry.Host both ref this)
    ScryPaths.cs      HomeDir / ConfigFile / DefaultLogsDir
    ScryConfig.cs     Load ~/.scry/scry.config.json
    ScryLogging.cs    Resolve + AddScryFile extension
    ScryFileLoggerProvider.cs  ILoggerProvider writing to a single timestamped file
  Scry.Analysis/      ClrMD integration: single-threaded AnalysisWorker, DumpSession, IAnalysisCommand<T>
src/Scry.Contracts/
  ScrySessions.cs     Session registry: Register / Unregister / List / IsAlive
```

## Milestones

- **M0 — skeleton (done):** host + gRPC over UDS/named pipe, `Health`/`Shutdown`, CLI prints JSON,
  idle shutdown. No ClrMD.
- **M1 — session model + logging (done):** `analyze`/`ps`/`health`/`stop`/`kill` commands, session
  registry, auto-spawn + readiness polling, `-v` file logging via `ILogger`/DI. No ClrMD yet.
- **M1b — dump loading + analysis engine (done):** `DataTarget.LoadDump`, DAC resolution,
  single-threaded `AnalysisWorker` in dedicated `Scry.Analysis` library, readiness reporting
  with runtime version. See [ADR 0006](docs/adr/0006-analysis-engine.md).
- **M2 — first analysis commands (done):** `ClrStack` (walk managed thread stacks).
- **M3 — heap walks (done):** `DumpHeap` (stats + paged object listing), `DumpExceptions`
  (paged exception addresses + type/message), `PrintException` (detail + stack trace).
  Immutable `HeapSnapshot` cache built once on analysis thread, served DAC-free for stats/paging.
- **M4 — distribution (done):** Single binary (`scry`) with hidden `__host` mode. Global tool
  packaging, self-contained per-RID zips, tag-triggered release CI. Symbol path config. See
  [ADR 0007](docs/adr/0007-single-binary-and-distribution.md) and
  [ADR 0008](docs/adr/0008-dac-and-symbol-resolution.md).
- **M5 — object inspection (done):** `DumpObject` (walk an object's fields), `DumpArray` (paged
  array element listing). Both return `{ "found": false }` for invalid addresses. Unit tests and
  smoke test integration complete.
- **M6 — collections:** `DumpConcurrentDictionary`, `DumpConcurrentQueue`.
- **M7 — hardening:** error-model polish, limit caps, broader cross-platform DAC robustness.
