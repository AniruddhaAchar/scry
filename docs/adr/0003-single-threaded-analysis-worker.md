# ADR 0003 — Serialize all ClrMD access onto one analysis worker

- **Status:** Accepted (implemented from M1)
- **Date:** 2026-06-10

## Context

ClrMD and the DAC it drives are **not thread-safe**. gRPC handlers, by contrast, run concurrently
on the thread pool. If two handlers touched the same `ClrRuntime`/`ClrHeap` at once, the DAC would
corrupt or crash.

Analysis commands also vary wildly in cost: a `dumpobj` is microseconds, a full-heap `dumpheap` can
walk millions of objects. A long command must not be able to wedge the process, and a client
deadline or disconnect must be able to abort an in-flight heap walk.

## Decision

A single dedicated **analysis worker thread** owns the `ClrRuntime`. gRPC handlers do not touch
ClrMD directly: each builds a work item (the command + parsed request + a `CancellationToken`),
enqueues it onto the worker, and awaits the result.

- The worker executes one item at a time, enforcing serialized DAC access.
- The request's `CancellationToken` is honored *inside* the ClrMD enumeration, so a deadline or
  disconnect aborts a long walk rather than blocking the queue behind it.
- ClrMD services (`ClrRuntime`, `ClrHeap`, helpers) are registered as **scoped** services in a
  per-dump DI scope; commands resolve from that scope. This is our equivalent of dotnet-dump's
  `[ServiceImport]` scoped-injection pattern, using standard `Microsoft.Extensions.DependencyInjection`
  scopes — we do not depend on `Microsoft.Diagnostics.DebugServices`.

## Consequences

- Correctness: no concurrent DAC access, by construction.
- Throughput on a single host is serial — acceptable, because the dominant cost is the one-time
  dump load, and a host serves a single dump. Concurrency, if ever needed, comes from running
  multiple hosts (one per dump), not multiple threads per host.
- Every enumerating command must thread its `CancellationToken` all the way into the ClrMD loop;
  this is a contract requirement, not an optimization.

## Status note

M0 (the skeleton) has no ClrMD and therefore no worker yet; `Health`/`Shutdown` run inline on the
gRPC thread. The worker is introduced in M1 alongside dump loading.
