# CLAUDE.md

Guidance for Claude Code (and humans) working in this repo.

## What scry is

`scry` is a ClrMD-based .NET memory-dump analyzer built **for AI agents**: it returns
**structured JSON**, not human-readable SOS text. It is two processes:

- **`scryd`** — a long-lived host daemon. Loads one dump with ClrMD, holds the `ClrRuntime`
  open, and serves analysis commands over gRPC. Loading is expensive (seconds, GBs of RAM);
  keeping it warm is the whole reason the daemon exists.
- **`scry`** — a short-lived, stateless CLI. An agent invokes it per command. It connects to
  (or, from M1, spawns) the `scryd` host for the target dump, issues one gRPC call, prints the
  JSON response, and exits.

The two binaries are named exactly `scry` and `scryd` on disk (set via `<AssemblyName>`), so what
an agent types matches what ships.

## Layout

```
scry.slnx                    Solution (modern XML format)
Directory.Build.props        Shared MSBuild settings (TFM, nullable, lock files, warnings-as-errors)
Directory.Packages.props     Central Package Management — all package versions live here
global.json                  Pins the .NET SDK band
src/
  Scry.Contracts/            Shared gRPC contract (Protos/scry.proto) + ScryEndpoint naming
  Scry.Host/                 scryd daemon (AssemblyName = scryd; Microsoft.NET.Sdk.Web)
  Scry.Client/               scry CLI    (AssemblyName = scry; System.CommandLine)
tests/
  Scry.UnitTests/            xUnit v3. Every test is [Trait("Category","Unit")].
docs/
  adr/                       Architecture Decision Records
  usage/                     Usage docs
```

## Build / test / run

```bash
dotnet build scry.slnx
dotnet test  scry.slnx --filter Category=Unit       # fast, dump-free unit tests
dotnet format scry.slnx --verify-no-changes         # what the pre-commit hook enforces

# End-to-end (M1 session model): spawn a host, query it, stop it.
# SCRYD_PATH lets the client find scryd when the two binaries are in separate build dirs.
export SCRYD_PATH=/path/to/scryd[.exe]
scry analyze [-v] <path/to/dump>   # spawns scryd, waits for READY, prints handle
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

Both binaries write a per-process log file to `~/.scry/logs/` (configurable via
`~/.scry/scry.config.json`). The `-v`/`--verbose` flag forces `Debug` level. On `scry analyze -v`,
the `-v` is forwarded to the spawned `scryd`. Stdout always stays pure JSON — logs go to the file
only. See [ADR 0005](docs/adr/0005-configuration-and-logging.md).

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
  registry, scryd auto-spawn + readiness polling, `-v` file logging via `ILogger`/DI. No ClrMD yet.
- **M1b — dump loading + analysis engine (done):** `DataTarget.LoadDump`, DAC resolution,
  single-threaded `AnalysisWorker` in dedicated `Scry.Analysis` library, readiness reporting
  with runtime version. See [ADR 0006](docs/adr/0006-analysis-engine.md).
- **M2 — first analysis commands (in progress):** `ClrStack` (walk managed thread stacks), then
  `DumpObject`, `ClrThreads`, `DumpStackObjects`.
- **M3 — heap walks:** `DumpHeap`, `DumpExceptions`, `PrintException` (pagination + cancellation).
- **M4 — collections:** `DumpConcurrentDictionary`, `DumpConcurrentQueue`.
- **M5 — hardening:** error-model polish, limit caps, per-RID release CI.
