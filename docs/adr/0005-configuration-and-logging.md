# ADR 0005 — Configuration and logging

- **Status:** Accepted
- **Date:** 2026-06-10

## Context

M0 shipped with no logging configuration and no log file: `scryd` wrote everything to stderr
(acceptable for interactive debugging) and `scry` wrote nothing (logs would pollute the pure-JSON
stdout an agent reads). As the session model grows (spawn, health polling, stop/kill), both binaries
need observable, structured logs that survive process exit — without compromising stdout purity on
the client.

## Decision

### Config file

A single JSON config file lives at `~/.scry/scry.config.json`. It is optional; missing file or
parse error silently yields all-defaults. Format (camelCase, comments/trailing-commas allowed):

```json
{
  "logging": {
    "folder": "/custom/log/dir",   // default: ~/.scry/logs
    "level":  "Debug"              // default: Information
  }
}
```

The config is loaded at startup by both `scry` and `scryd` via `ScryConfig.Load()` in `Scry.Core`.

### Per-process timestamped log files

Each process writes one log file:

```
~/.scry/logs/{appName}-{yyyyMMdd-HHmmss}-{pid}.log
```

The per-process, timestamped file name avoids locking conflicts between concurrent invocations and
makes it easy to correlate logs with a specific session (pid matches the session descriptor).

Log files are not rotated or size-capped in v0.0.1 — this is deferred to a later milestone.

### Microsoft ILogger via DI + a custom file provider

A small `ScryFileLoggerProvider : ILoggerProvider` wraps a `StreamWriter` opened with
`FileShare.Read` so the file can be tailed while the process runs. All writes are guarded by a
`Lock` (the .NET 10 `System.Threading.Lock`), safe for multi-threaded gRPC host use.

`Scry.Core` exposes:
- `ScryLogging.Resolve(appName, verbose, config)` → `ResolvedLogging` (level, folder, filePath)
- `ILoggingBuilder.AddScryFile(resolved)` — extension that registers the provider and sets the
  minimum level.

### Applies to both binaries

**`scryd`** keeps its existing stderr console logger and additionally calls `AddScryFile`. The
console path is useful for interactive debugging; the file path survives the terminal session.

**`scry`** uses a `ServiceCollection`-backed DI container (`Bootstrap.Build(verbose)`) that
registers `ILogger<ScryCommands>` backed solely by the file provider. Stdout remains pure JSON.

### `-v` / `--verbose` flag

Both binaries accept `-v` / `--verbose`. When set, the effective log level is forced to
`LogLevel.Debug` regardless of config. `scry analyze` forwards `--verbose` to the `scryd` it
spawns, so a single `-v` on the client also enables debug logging on the host.

### Level precedence

`-v` → `LogLevel.Debug` > config `level` string (case-insensitive) > `LogLevel.Information`

## Consequences

- **stdout stays pure JSON** on the client — logs go to the file only. Agents can parse stdout
  without stripping log lines.
- Both binaries share config/logging infrastructure through `Scry.Core`, avoiding duplication.
- Log files accumulate in `~/.scry/logs/` until manually cleaned; no automatic rotation in v0.0.1.
- The `ILogger` abstraction leaves the door open to adding sinks (e.g. structured JSON, rolling
  files) without changing the call sites.
- `SCRY_SESSIONS_DIR` env var overrides the registry dir for tests (no production impact).
