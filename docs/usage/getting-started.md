# Getting started

This walks through the M1 session model: collecting a dump, analyzing it, and walking managed
thread stacks. The `scry` CLI spawns itself in internal host mode automatically and queries it
over a local transport (named pipe on Windows, Unix domain socket on Linux/macOS).

## Prerequisites

- **Install option 1: Global tool** — `dotnet tool install -g Scry.Cli` (requires ASP.NET Core 10 runtime).
- **Install option 2: Self-contained** — Download a per-RID zip from [GitHub Releases](https://github.com/acrrd/scry/releases) and extract it.
- **Development option** — Build locally: `dotnet build scry.slnx`. The single `scry` executable lands under `src/Scry.Client/bin/<config>/net10.0/`.
- **dotnet-dump** tool (for collecting fixture dumps): `dotnet tool install -g dotnet-dump`.

## Concepts

- **One host per dump.** A host process serves exactly one dump. The endpoint it listens on is
  derived from the dump path (`scry-<hash>`), so every `scry` command for that dump reaches the
  same host.
- **Auto-spawn.** `scry analyze <dump>` spawns the host (itself in internal `__host` mode) if needed
  and polls for readiness. No manual startup required.
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

The `scry` CLI spawns itself in internal host mode (`scry __host ...`), loads the dump, and
returns a handle. Subsequent commands default to this session and need no `--dump` argument.

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

## Heap queries

Heap queries build a one-time immutable snapshot of the managed heap on first use, then serve
subsequent queries (statistics, paging, exception lookup) without touching ClrMD. The snapshot
is fast and reusable for the lifetime of the session.

### Heap statistics

```bash
scry dumpheap
```

```json
{
  "stats": [
    {
      "type": "System.String",
      "methodTable": "0x7f9a12345678",
      "count": 1250,
      "totalSize": 45000
    },
    {
      "type": "System.Object[]",
      "methodTable": "0x7f9a87654321",
      "count": 320,
      "totalSize": 32000
    }
  ]
}
```

Each entry shows the type name, method table address, object count, and total size in bytes.
Entries are sorted by `totalSize` descending.

### Objects of a specific type (paged)

```bash
scry dumpheap --type System.String --limit 10
```

```json
{
  "objects": [
    {
      "address": "0x7f9a00001234",
      "type": "System.String",
      "size": 36
    },
    {
      "address": "0x7f9a00005678",
      "type": "System.String",
      "size": 45
    }
  ],
  "totalMatches": 1250,
  "truncated": true
}
```

The `totalMatches` field shows how many objects match the filter before paging; `truncated`
indicates whether there are more results. Use `--offset` to walk pages.

### Live exceptions

```bash
scry dumpexceptions
```

```json
{
  "exceptions": [
    {
      "address": "0x7f9a0000abcd",
      "type": "System.InvalidOperationException",
      "message": "Object reference not set to an instance of an object.",
      "hresult": -2146233088,
      "inner": []
    }
  ],
  "totalMatches": 1,
  "truncated": false
}
```

Each exception lists its address, type, message, HResult, and any inner exception chain.

### Exception detail with stack trace

```bash
scry printexception --address 0x7f9a0000abcd
```

```json
{
  "found": true,
  "exception": {
    "address": "0x7f9a0000abcd",
    "type": "System.InvalidOperationException",
    "message": "Object reference not set to an instance of an object.",
    "hresult": -2146233088,
    "inner": []
  },
  "stackTrace": [
    {
      "kind": "ManagedMethod",
      "instructionPointer": "0x7f9a00123456",
      "stackPointer": "0x1000",
      "method": "Main",
      "type": "Program",
      "module": "app.dll"
    }
  ]
}
```

The stack trace captures the reconstructed managed call stack at the point where the exception
was thrown.

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
or `--dump`.
