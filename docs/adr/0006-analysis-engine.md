# ADR 0006 — Analysis engine

- **Status:** Accepted
- **Date:** 2026-06-11

## Context

M1 shipped with a session model (spawn, health polling, stop/kill) and logging infrastructure,
but the core ClrMD analysis work was deferred. Now we need to load dumps, access the CLR
runtime, and serve analysis commands — all while maintaining the constraint that ClrMD/DAC is
fundamentally single-threaded and not reentrant.

## Decision

### Dedicated Scry.Analysis library

ClrMD integration code lives in a new `Scry.Analysis` library, separate from gRPC hosting
and client concerns. This library is a pure analysis layer: no HTTP, no gRPC, no process
management. It can be reused by any future front-end (CLI, API, batch tools) without
pulling in networking dependencies.

### Single-threaded analysis worker

All ClrMD access is serialized onto exactly ONE dedicated thread (`AnalysisWorker`).

- `AnalysisWorker` runs an async loop that dequeues `IAnalysisCommand<TResult>` work items,
  executes them on its own thread (calling `dataTarget` and `runtime` APIs), and completes
  the associated `Task<TResult>`.
- gRPC handlers (in `Scry.Host`) do not call ClrMD directly. Instead, they construct a
  command, enqueue it to `AnalysisWorker`, and `await` the result.
- This design enforces the single-threaded invariant transparently and avoids deadlocks or
  race conditions on DAC state.

See [ADR 0003](docs/adr/0003-single-threaded-analysis-worker.md) for the full thread-safety
rationale.

### Dump loading and DumpSession

`DumpSession` owns and manages the lifecycle of `DataTarget` and `ClrRuntime`:

- Constructed by `AnalysisWorker` when the session is created (on the analysis thread for
  DAC affinity).
- The constructor validates the dump's architecture: x64 and arm64 are accepted in v0.0.1;
  x86 dumps are rejected.
- Exposes properties (`Runtime`, `DataTarget`) for read access by commands.
- Disposed when the session is torn down.

### Commands

Commands implement `IAnalysisCommand<TResult>` with a single method:

```csharp
TResult Execute(DumpSession session);
```

Concrete commands (e.g. `ClrStackCommand`) are stateless; each execution receives a fresh
`DumpSession` reference and does not mutate shared state. This keeps commands composable
and testable.

The first command is `ClrStackCommand`:

- Input: `ClrStackRequest` (thread OS id, or all threads).
- Output: `ClrStackResponse` containing a list of `ThreadStack` (each with OS thread id,
  managed thread id, alive status, and a list of `StackFrame`s).
- Each frame captures: kind (e.g. `ManagedMethod`), instruction pointer, stack pointer,
  method name, type, and module.

### DAC matching and symbol resolution

- The DAC (Debug Assistance Component) bundled with `DataTarget.LoadDump` must match the
  dump's runtime version. If there is a mismatch, `LoadDump` raises an exception.
- In v0.0.1, the DAC is resolved from the LOCAL machine only. Cross-machine analysis
  (requiring a symbol server or `dotnet-symbol`) is a later milestone.
- Analysis must happen on the same host that produced the dump.

### Ready state reporting

A `DumpSession` is considered `READY` only after:

1. `DataTarget.LoadDump(path)` succeeds.
2. DAC resolution completes.
3. `ClrRuntime` is obtained.

Until then, the session stays in `LOADING` state. Once `READY`, `HealthResponse.RuntimeVersion`
is populated with the CLR version string.

### Immutable heap snapshot cache (M3)

In M3, heap analysis commands build an immutable `HeapSnapshot` once on the analysis thread
and serve all subsequent heap queries (statistics, paging, exception lookup) from it without
touching ClrMD. This is safe and efficient because:

- The snapshot is built once by the single-threaded `AnalysisWorker`, ensuring DAC safety.
- All query methods (`Stat`, `Objects`, `ExceptionAddresses`) are pure (no ClrMD calls),
  so they are reentrant and fast.
- Statistics and paging queries are served directly from the snapshot.
- Per-exception detail and stack traces (`PrintException`) read ClrMD on demand, using the
  snapshot's address list for fast lookup.

The snapshot holds parallel arrays: type names (interned in a small table), addresses, type
indices, sizes, and exception positions. This compact representation keeps millions of objects
efficient in memory.

## Consequences

- **ClrMD safety:** The single-threaded `AnalysisWorker` prevents data races and DAC corruption.
  All analysis commands execute serially on the same thread, with no locks needed on the
  session or runtime objects themselves.
- **Decoupled architecture:** `Scry.Analysis` has no network or process-management code,
  making it reusable and testable in isolation.
- **Dump locality:** v0.0.1 analysis only works when `scry` and `scryd` run on the same
  machine as the dump. Cross-machine analysis is deferred to a later milestone.
- **Extensibility:** New analysis commands are added by implementing `IAnalysisCommand<T>`;
  they automatically inherit the thread-safety and DAC affinity guarantees.
- **Heap query performance:** The immutable snapshot makes heap statistics, paging, and
  exception filtering fast and repeatable, suitable for agent-driven iterative analysis.
