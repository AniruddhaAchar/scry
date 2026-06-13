# Symptom playbooks

One recipe per symptom class. Each gives the orient signals that point here, a starting hypothesis,
the command sequence, what **confirms** vs **refutes**, and the **typical INSUFFICIENT** cases — the
specific ways this dump may simply not hold the answer, so you stop instead of spinning.

Pick the playbook from your orient pass (`clrthreads`, `dumpexceptions`, `dumpheap --stat`, `stack`).
If the signals don't match any cleanly, the symptom is **unclear** — say so and gather a little more,
but the no-progress counter and step budget still apply.

---

## Crash / unhandled exception

**Points here:** `dumpexceptions` shows live exceptions, and/or a thread in `clrthreads` carries a
`currentException`. The dump was likely taken at the moment of an unhandled throw.

1. `dumpexceptions` → find the exception(s); note `address`, `type`, `message`.
2. `printexception --address <addr>` → full message, `hResult`, inner chain, and stack trace.
3. Corroborate: `clrthreads` to find the thread whose `currentException` matches; `stack --thread
   <os-id>` to see the faulting call path.
4. If the message references an object/value, `dumpobject` it for state at the fault.

**Confirms:** one exception whose stack trace ends at application code, matching a faulting thread.
**Refutes / pivot:** the only exceptions are benign/handled (e.g. a cancellation), or none are tied
to a faulting thread → this may be a hang, not a crash. Pivot.

**Typical INSUFFICIENT:** exceptions linger on the heap after being caught, so presence alone doesn't
prove the fatal one; if no thread carries it and no stack ties it to a fault, you can't conclude it
crashed the process from this dump. Stop and ask for the process exit reason / first-chance logs.

---

## Hang or deadlock (monitors / locks)

**Points here:** `dumpexceptions` is empty; `clrthreads` shows many `Blocked`/waiting threads and the
process is alive but stuck; stacks sit in `Monitor.Enter`/`Wait`.

1. `clrthreads` → how many threads, which look blocked.
2. `syncblk` → which monitors are held, by whom (`owner`), and who's waiting (`waitingThreadCount`).
3. `stack --thread <owner-os-id>` and `stack --thread <waiter-os-id>` → see what each holds and what
   it's trying to acquire.
4. Look for a cycle: A holds L1 wants L2; B holds L2 wants L1.

**Confirms:** a lock-ownership cycle across two+ threads (the AB/BA pattern), each stack blocked
acquiring the lock the other owns.
**Refutes / pivot:** `syncblk` shows no contended monitors. The block is on something else —
`SemaphoreSlim`, `Task.Wait()/.Result` (sync-over-async), `ManualResetEventSlim`, a native call, or
an async await. Pivot: if stacks show `Task.Wait`/`GetResult`, go to **async hang**; otherwise read
the blocking frame in `stack`.

**Typical INSUFFICIENT:** the dump was taken after the hang cleared (threads now idle); or the wait
is on a native/OS primitive scry can't introspect (the managed stack just shows a P/Invoke). Report
what's blocked and recommend a dump taken *during* the hang, or native-debugger inspection.

---

## Async hang

**Points here:** the app is stalled but `syncblk` shows no deadlock, and stacks are shallow / sitting
in the thread-pool — work isn't progressing but no OS thread is obviously blocked.

1. `dumpasync` → list state machines; focus on `status: "suspended at await N"`.
2. Read the suspended `type`s — which `async` methods are parked, and at which await.
3. Follow `continuation` chains: many machines awaiting the same downstream task/resource point at
   the real culprit (an un-completed `TaskCompletionSource`, a never-returning I/O, an `await` on a
   task that will never finish).
4. If a suspended machine awaits a specific object, `dumpobject` it for state.

**Confirms:** one or a cluster of state machines suspended at an await that will never complete (e.g.
all waiting on the same TCS / connection / channel).
**Refutes / pivot:** all machines are `running`/`completed`, or none exist → not an async stall.
Pivot to **hang/deadlock** or re-examine `stack` for sync-over-async (`Task.Result` on a pool thread
starving the pool).

**Typical INSUFFICIENT:** the awaited resource lives outside the process (a remote call, a DB), so the
dump shows *that* you're waiting but not *why the other end* never answers. Report the parked awaits
and point at the external dependency.

---

## Memory leak / high memory / OutOfMemory

**Points here:** `dumpheap --stat` shows one or few types dominating `totalSize`, and/or
`dumpexceptions` shows `OutOfMemoryException`, and/or the dump is very large.

1. `dumpheap --stat` → the dominant type(s) by `totalSize` and `count`.
2. `dumpheap --type <DominantType>` → grab a representative `address`.
3. `gcroot --address <addr>` → the retaining root. `--max-paths` for multiple retainers.
4. Walk the `chain`: a `static` field, an event handler, a cache, a long-lived collection holding
   references it should have released.

**Confirms:** a dominant type whose `gcroot` chain runs to a single retaining root (static/cache/
event), explaining unbounded growth.
**Refutes / pivot:** the dominant type is `gcroot`'d to `rooted: false` (it's collectible — not a
leak, GC just hasn't run), or its roots are ordinary/expected. Pivot: the memory may be in native/
unmanaged allocations, fragmentation (LOH), or simply legitimate working set — the managed heap
doesn't explain it.

**Typical INSUFFICIENT:** a single dump shows a *level*, not a *trend* — high memory in one snapshot
isn't proof of a leak. If roots look legitimate and nothing dominates pathologically, say so and
recommend two dumps over time (or a `dotnet-gcdump` diff) to establish growth.

---

## High CPU

**Points here:** the report is "pegged CPU," threads are `running` (not blocked), no exceptions.

1. `clrthreads` → which threads are alive and not blocked.
2. `stack` (all) → look for many threads in the same hot method, a tight loop, or GC threads
   dominating (`isGc`), which would suggest GC pressure (loop back to the leak/OOM playbook).
3. If GC-related, `dumpheap --stat` for allocation pressure.

**Confirms:** a consistent hot frame across threads, or GC threads busy with a bloated heap.
**Refutes / pivot:** stacks are varied/idle → a single post-hoc dump can't see CPU time.

**Typical INSUFFICIENT (common here):** a memory dump is a *single instant*; CPU usage is a *rate over
time*. One dump rarely diagnoses high CPU. Unless a hot loop is blatant across threads, stop and
recommend a sampling profiler / CPU trace (`dotnet-trace`) instead — this is one of the most
important "the dump is the wrong tool" calls to make confidently.

---

## When nothing fits

If orient signals are ambiguous, state that plainly, gather one or two more cheap facts, and watch
the no-progress counter. Reaching INSUFFICIENT on an unclear dump — with a clear account of what you
checked and what artifact would actually answer the question — is a *good* outcome, not a failure.
