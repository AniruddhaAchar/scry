# Getting started

This walks through the M1 session model: collecting a dump, analyzing it, and walking managed
thread stacks. The `scry` CLI spawns a `scryd` host automatically and queries it over a local
transport (named pipe on Windows, Unix domain socket on Linux/macOS).

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` ≥ 10.0).
- **dotnet-dump** tool: `dotnet tool install -g dotnet-dump` (needed to capture fixture dumps).
- Build the binaries: `dotnet build scry.slnx`. They land as `scry` and `scryd` under each
  project's `bin/<config>/net10.0/`.

## Concepts

- **One host per dump.** A `scryd` process serves exactly one dump. The endpoint it listens on is
  derived from the dump path (`scry-<hash>`), so every `scry` command for that dump reaches the
  same host.
- **Auto-spawn.** `scry analyze <dump>` spawns the host if needed and polls for readiness. No
  manual host startup required.
- **Transport.** A named pipe on Windows, a Unix domain socket on Linux/macOS. No TCP port.
- **Output.** Every command prints JSON to stdout. Failures print a JSON `error` object and exit
  non-zero.

## Collect a dump

```bash
scripts/collect-dump.ps1 -ProcessId 1234
# Outputs: C:\Users\...\AppData\Local\Temp\scry-fixture-1234-20260611-143022.dmp
```

This script uses the globally installed `dotnet-dump` to capture a local memory dump. The dump
stays on the same machine that produced it (cross-machine symbol resolution is a later milestone).

## Analyze the dump

```bash
scry analyze C:\Users\...\AppData\Local\Temp\scry-fixture-1234-20260611-143022.dmp
```

```json
{
  "handle": "scry-8dab0db081f14f3e",
  "dumpPath": "C:\\Users\\...\\scry-fixture-1234-20260611-143022.dmp",
  "pid": 5678,
  "state": "READY",
  "runtimeVersion": "8.0.0"
}
```

The client spawns `scryd`, loads the dump, and returns a handle. Subsequent commands default to
this session and need no `--dump` argument.

## Walk managed thread stacks

```bash
# Every managed thread and its frames:
scry stack
```

```json
{
  "threads": [
    {
      "osThreadId": 1234,
      "managedThreadId": 1,
      "isAlive": true,
      "frames": [
        {
          "kind": "ManagedMethod",
          "instructionPointer": "0x7ff000001234",
          "stackPointer": "0x1000",
          "method": "Main",
          "type": "Program",
          "module": "app.dll"
        }
      ]
    }
  ]
}
```

```bash
# A single thread (by OS thread id):
scry stack --thread 1234
```

Each frame includes: kind (e.g. `ManagedMethod`), instruction pointer, stack pointer, managed
method name, declaring type, and containing module.

## Session management

```bash
# List live sessions:
scry ps

# Query health of the current session:
scry health

# Stop the session gracefully:
scry stop

# Force-kill (if graceful stop hangs):
scry kill
```

All commands default to the single active session. Explicitly target a session with `--handle`
or `--dump`. |
