---
name: scry-dump-analysis
description: >-
  Diagnose .NET memory dumps (.dmp) with the `scry` CLI — a ClrMD-based analyzer that emits
  structured JSON for agents. Use this whenever you have a .NET process dump and need to find a
  root cause: a crash, an unhandled exception, a hang or deadlock, an async hang, a managed memory
  leak, high memory / OutOfMemory, or "why is this object still alive." Trigger this skill for any
  mention of a .NET crash dump, .dmp/minidump, `dotnet-dump`, SOS/WinDbg-style analysis (clrstack,
  dumpheap, gcroot, syncblk, dumpasync), or requests like "analyze this dump", "why did my service
  crash/hang", "what's leaking memory", even if the user doesn't name scry. It also encodes a
  bounded reasoning loop that knows when to STOP and report the dump lacks enough information,
  instead of guessing — use it to avoid spinning in circles on dump analysis.
---

# scry: structured .NET dump analysis

`scry` analyzes a .NET memory dump and returns **structured JSON** (not human SOS text), so you can
parse every result and chain commands. This skill teaches you to drive it **and**, more importantly,
to reason about a dump with a bounded loop that converges on a cited root cause — or stops and says
the dump doesn't contain enough information, which is a correct and safe outcome, not a failure.

The single most important idea: **dump analysis is hypothesis-testing, not browsing.** Every command
you run should either confirm/refute a specific hypothesis or hand you a concrete next lead (an
address, a type, a thread id). Running commands "to look around" is how agents spin forever. Don't.

## Setup: the session model

Loading a dump with ClrMD is expensive (seconds, GBs of RAM), so scry runs a warm background host
per dump. You `analyze` once, run many queries against the warm session, then `stop`.

```bash
scry analyze /path/to/app.dmp     # loads the dump; prints handle + runtime version when READY
# ... run analysis commands (below) ...
scry stop                          # release the dump and shut the host down
```

- `analyze` is **idempotent** for the same dump and the host **idles out** on its own, so a missing
  `stop` is not catastrophic — but always `stop` when you're done to free memory.
- Analysis commands target the single active session automatically; you don't pass the dump path
  again. (If multiple sessions are live, pass the `handle` printed by `analyze`.)
- **Every command prints JSON to stdout.** Parse it. On failure you get
  `{ "error": { "code", "message", "hint" } }` and a non-zero exit — read the `hint`.

If `analyze` fails to reach READY, the dump can't be loaded at all (architecture mismatch — x86 is
rejected; or the matching runtime/DAC couldn't be resolved). That's an immediate, honest stop: you
cannot analyze this dump in this environment. Say so.

## Commands at a glance

Full synopsis, arguments, and JSON shapes are in **[references/commands.md](references/commands.md)** —
read it before using a command whose output you don't already know. Quick index:

| Command | Answers |
|---|---|
| `clrthreads` | What threads exist, their state, GC mode, and any in-flight exception. |
| `stack [--thread <os-id>]` | What each (or one) managed thread is executing. |
| `dumpexceptions` | What exceptions are live on the heap. |
| `printexception --address <hex>` | Full detail + stack trace for one exception. |
| `dumpheap` (`--stat` / `--type <substr>`) | Heap composition by type; or a paged object listing. |
| `dumpobject --address <hex>` | One object's fields. |
| `dumparray --address <hex>` | An array's elements (paged). |
| `gcroot --address <hex>` | Why an object is still alive (root paths). |
| `syncblk` | Held monitors + owner/waiters — monitor deadlocks. |
| `dumpasync` | Async state machines in flight — async hangs. |

Addresses are hex (`0x...`); copy them verbatim from one command's JSON into the next.

## The reasoning loop

Work this loop. Each pass must end by either continuing with a **new lead** or hitting a **stop
condition**. Keep a running tally of two things in your head: your **current hypothesis**, and a
**no-progress counter** (how many commands in a row produced nothing new).

### 1. Orient — cheap, wide signals first

Before touching a single address, gather the broad facts and **name the symptom class**. These four
are cheap and almost always informative:

```
clrthreads        → thread count, who's blocked, any thread carrying an exception
dumpexceptions    → are there live exceptions (crash) or none (likely hang/leak)?
dumpheap --stat   → is one type dominating memory (leak/OOM)?
stack             → what are threads actually doing?
```

Classify into one of: **crash / unhandled exception**, **hang or deadlock**, **async hang**,
**memory leak / high memory / OOM**, **high CPU**, or **unclear**. The symptom class selects a
playbook — see **[references/playbooks.md](references/playbooks.md)** for the command sequence,
the expected evidence, and the common dead ends for each.

### 2. Hypothesize — one falsifiable claim at a time

State a single, specific, falsifiable hypothesis grounded in the orient signals. Good hypotheses
name concrete entities:

- "Thread 7 is deadlocked waiting on a monitor held by thread 12."
- "`MyCache` is leaking; it's rooted by a static and holds most of the heap."
- "The request faulted with the `InvalidOperationException` visible in `dumpexceptions`."

One at a time. Multiple vague hunches are how you lose the thread.

### 3. Test — the single command that would settle it

Pick the *one* command that confirms or kills the hypothesis, and predict what each outcome looks
like before you run it. `gcroot <addr>` to prove a retainer; `stack <tid>` to see what a thread
blocks on; `syncblk` to find the lock owner; `printexception <addr>` for the fault detail;
`dumpasync` for the parked await.

### 4. Progress check — the spin-breaker

After the command, ask one question: **did this produce a new concrete lead (an address, type, or
thread id) or change my confidence in the hypothesis?**

- **Yes** → reset the no-progress counter; either you've confirmed enough (go to SOLVED) or you have
  a new lead to test (loop to step 2/3 with it).
- **No** → increment the no-progress counter. You learned nothing actionable.

Hard rules that keep the loop honest — internalize the reasoning, don't just obey:

- **Never run the same command with the same arguments twice.** The dump is immutable; the answer
  won't change. If you're tempted to re-run, that's a no-progress signal telling you you're stuck,
  not a retry.
- **Every step must consume or produce a concrete lead.** A command run with no hypothesis behind
  it is browsing, and counts as no-progress.
- **A confident wrong root cause is worse than an honest "not enough information."** Whoever reads
  your diagnosis will act on it. Guessing sends them down a wrong path; an honest stop tells them
  exactly what to capture next.

### 5. Stop — every loop ends at one of these three

You are **done** when you reach one of these. Don't continue past a stop condition looking for more.

- ✅ **SOLVED** — you can name the root cause **and cite the evidence**: the exact command you ran
  and the field/value in its JSON that proves it. No citation ⇒ not solved, keep going. Report the
  cause, the evidence, and the fix or next step.

- 🛑 **INSUFFICIENT (the safe default)** — declare that the dump doesn't contain enough information
  when **any** of these trips:
  - the **no-progress counter reaches 3** consecutive non-advancing commands;
  - you've spent a **step budget** of roughly **12 commands** without a cited root cause;
  - the evidence you'd need is **provably absent** — e.g. no exception object for a "crash", locals
    optimized away, no symbols for the relevant module, the suspect type isn't on the heap, or
    `gcroot` returns `rooted: false`.

  This is a first-class outcome. Report: what you **did** establish, what you **ruled out**, and —
  most usefully — **what additional artifact would answer the question** (e.g. "a dump captured
  *during* the hang, not after", "symbols for `MyApp.Native.dll`", "a server GC heap dump at peak
  memory"). Stopping here is the correct call far more often than agents assume.

- ↩️ **PIVOT** — the hypothesis was refuted but the test surfaced a new concrete lead. Replace the
  hypothesis with one built on the new lead, **reset the no-progress counter**, and loop. A pivot is
  progress; distinguish it sharply from spinning (which produces no new lead).

## Worked example (shape of a good session)

```
Goal: "the service hung; here's a dump."
1. clrthreads                  → 40 threads; thread 12 has no exception; many threads Blocked.   [orient]
2. dumpexceptions              → none live.  → not a crash; hypothesis: monitor deadlock.         [orient → hypothesize]
3. syncblk                     → object O monitorHeld by thread 12, waiters include thread 7.     [test → new lead]
4. stack --thread 12           → thread 12 is blocked acquiring a *second* lock.                  [test → confirms]
5. stack --thread 7            → thread 7 holds that second lock, waiting on O. AB/BA deadlock.   [test → confirms]
→ SOLVED: classic two-lock ordering deadlock between threads 7 and 12 (evidence: syncblk owner +
  the two stacks). Fix: impose a consistent lock order.
```

And the shape of a good give-up:

```
Goal: "intermittent slowness; here's a dump taken after it recovered."
1. clrthreads / stack          → all threads idle in the thread-pool.        [orient]
2. dumpexceptions              → none.                                       [orient]
3. dumpheap --stat             → nothing dominant; heap is small.            [no-progress 1]
4. gcroot on the largest type  → ordinary, expected roots.                   [no-progress 2]
→ INSUFFICIENT: the dump was captured after the slowdown resolved, so the pathological state isn't
  present. To diagnose, capture a dump *while* the slowness is happening (or collect CPU traces).
```

## Reference files

- **[references/commands.md](references/commands.md)** — every command: arguments, the exact JSON
  shape it returns, and gotchas. Consult before relying on a command's output.
- **[references/playbooks.md](references/playbooks.md)** — per-symptom recipes (crash, hang/deadlock,
  async hang, leak/OOM, high CPU): orient signals → hypothesis → commands → what confirms vs.
  refutes → the typical INSUFFICIENT cases for that symptom.
