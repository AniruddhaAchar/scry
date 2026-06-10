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

# End-to-end (M0): start a host, then talk to it. The dump path need not exist yet in M0 —
# it is only used to derive the endpoint.
scryd --dump <path> [--idle-timeout <minutes>]      # in one terminal
scry health   --dump <path>
scry shutdown --dump <path>
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

## Milestones

- **M0 — skeleton (done):** host + gRPC over UDS/named pipe, `Health`/`Shutdown`, CLI prints JSON,
  idle shutdown. No ClrMD.
- **M1 — dump loading:** `DataTarget.LoadDump`, DAC resolution, single-threaded analysis worker,
  spawn-on-miss + endpoint discovery, readiness reporting.
- **M2 — cheap reads:** `DumpObject`, `ClrThreads`, `ClrStack`, `DumpStackObjects`.
- **M3 — heap walks:** `DumpHeap`, `DumpExceptions`, `PrintException` (pagination + cancellation).
- **M4 — collections:** `DumpConcurrentDictionary`, `DumpConcurrentQueue`.
- **M5 — hardening:** error-model polish, limit caps, logging, per-RID release CI.
