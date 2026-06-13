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

## Managed threads

`stack` walks frames; `clrthreads` lists the threads themselves with their runtime state — the
triage view SOS prints as `!Threads`.

```bash
scry clrthreads
```

```json
{
  "handle": "scry-58d03c61c2a27baf",
  "threads": [
    {
      "osThreadId": 25940,
      "managedThreadId": 2,
      "isAlive": true,
      "isBackground": false,
      "isFinalizer": false,
      "isGc": false,
      "gcMode": "Preemptive",
      "lockCount": null,
      "state": ["TS_CoInitialized", "TS_InMTA"],
      "currentException": null
    }
  ]
}
```

Each thread reports its OS and managed ids, liveness, background/finalizer/GC role, GC mode
(`Cooperative`/`Preemptive`), the decoded `ClrThreadState` flag names, and — if the thread is
currently throwing — a shallow `currentException` (`{ address, type, message }`). `lockCount` is
`null` when the runtime doesn't track it for that thread.

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

### Object inspection

```bash
scry dumpobject --address 0x7f9a00001234
```

```json
{
  "found": true,
  "address": "0x7f9a00001234",
  "type": "System.String",
  "methodTable": "0x7f9a12345678",
  "size": 36,
  "fields": [
    {
      "name": "_length",
      "type": "System.Int32",
      "offset": 8,
      "value": "5"
    },
    {
      "name": "_firstChar",
      "type": "System.Char",
      "offset": 12,
      "value": "\"Hello\""
    }
  ]
}
```

Walk an object's fields by address (similar to SOS `!DumpObj`). Returns `{ "found": false }` if the
address is not a valid object. Use `dumpheap --type` to find object addresses.

### Array inspection (paged)

```bash
scry dumparray --address 0x7f9a00005678 --limit 10
```

```json
{
  "found": true,
  "address": "0x7f9a00005678",
  "type": "System.Int32[]",
  "elementType": "System.Int32",
  "length": 100,
  "truncated": true,
  "elements": [
    {
      "index": 0,
      "value": "42"
    },
    {
      "index": 1,
      "value": "99"
    }
  ]
}
```

Walk an array's elements by address, paged (similar to SOS `!DumpArray`). Like `dumpheap`, it
returns element values truncated to a maximum length and quoted. Returns `{ "found": false }` if
the address is not a valid array. Use `--limit` and `--offset` for pagination.

### GC roots (why an object is alive)

```bash
scry gcroot --address 0x14b0dc00208            # first root path
scry gcroot --address 0x14b0dc00208 --max-paths 5
```

```json
{
  "found": true,
  "target": "0x14b0dc00208",
  "rooted": true,
  "truncated": true,
  "roots": [
    {
      "rootKind": "StrongHandle",
      "rootAddress": "0x14b098513e8",
      "stackFrame": null,
      "chain": [
        { "address": "0x14b0b400028", "type": "System.Object[]" },
        { "address": "0x14b0dc00208", "type": "System.Collections.Generic.Dictionary<System.String, System.Object>" }
      ]
    }
  ]
}
```

Finds the GC root paths that keep an object alive (similar to SOS `!GCRoot`) — the core "why
isn't this collected / what's leaking" query. Each path names the `rootKind` (`StrongHandle`,
`Stack`, `FinalizerQueue`, `PinnedHandle`, …), the root address, the originating `stackFrame`
(for `Stack` roots), and the `chain` of objects from the root down to the target.

`rooted` is `false` for a live-but-unreferenced object; `{ "found": false }` means the address
isn't a valid object. This walks a reverse object graph over the whole heap and is the most
expensive command, so it defaults to **one** path (`--max-paths` raises the cap; `truncated`
signals more paths exist) and to a longer 120s timeout.

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
