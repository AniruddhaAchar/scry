# scry command reference

Every command writes indented camelCase JSON to stdout. `null` fields are omitted. Failure prints
`{ "error": { "code", "message", "hint" } }` and exits non-zero. Addresses are hex strings
(`"0x1a2b3c"`); pass them back verbatim with `--address`.

Common options: `--handle <scry-...>` targets a specific session (only needed when several are
live); `--timeout <seconds>` bounds the RPC (0 = no timeout). Paged commands take `--limit` and
`--offset`.

## Table of contents
- [Session: analyze, ps, health, stop, kill](#session)
- [clrthreads](#clrthreads)
- [stack](#stack)
- [dumpexceptions](#dumpexceptions)
- [printexception](#printexception)
- [dumpheap](#dumpheap)
- [dumpobject](#dumpobject)
- [dumparray](#dumparray)
- [gcroot](#gcroot)
- [syncblk](#syncblk)
- [dumpasync](#dumpasync)

---

## Session

```bash
scry analyze <dump-path>     # load a dump; spawns a warm host, waits for READY
scry ps                      # list live sessions
scry health                  # readiness/runtime of the active session
scry stop                    # graceful shutdown (releases the dump)
scry kill                    # force-terminate
scry version                 # scry version + host runtime/os/arch (no session needed)
```

`analyze` success:
```json
{ "handle": "scry-64c34baa...", "dumpPath": "...", "pid": 29220, "state": "READY", "runtimeVersion": "10.0.526.15411" }
```
A non-READY result (or an error) means the dump cannot be loaded here (x86 is unsupported; the
runtime/DAC may be unresolvable). That is a hard stop for analysis.

---

## clrthreads
List managed threads with runtime state. **Start here for hangs/deadlocks.**
```bash
scry clrthreads
```
```json
{
  "handle": "scry-...",
  "threads": [
    {
      "osThreadId": 25940, "managedThreadId": 2,
      "isAlive": true, "isBackground": false, "isFinalizer": false, "isGc": false,
      "gcMode": "Preemptive", "lockCount": null,
      "state": ["TS_CoInitialized", "TS_InMTA"],
      "currentException": { "address": "0x...", "type": "System.OperationCanceledException", "message": "..." }
    }
  ]
}
```
- `currentException` is present only when that thread is actively throwing — a strong lead.
- `lockCount` is `null` when the runtime doesn't track it (common); don't read meaning into that.
- Use `osThreadId` with `stack --thread`.

## stack
Managed stack frames for all threads, or one.
```bash
scry stack
scry stack --thread 25940
```
```json
{
  "handle": "scry-...",
  "threads": [
    { "osThreadId": 25940, "managedThreadId": 2, "isAlive": true,
      "frames": [
        { "kind": "ManagedMethod", "ip": "0x...", "sp": "0x...",
          "method": "Wait", "type": "System.Threading.Monitor", "module": "System.Private.CoreLib.dll" }
      ] }
  ]
}
```
- A frame blocked in `Monitor.Wait`/`Monitor.Enter` ⇒ cross-reference `syncblk`.
- `method`/`type`/`module` are `null` when symbols are missing — itself a possible INSUFFICIENT signal.

## dumpexceptions
Live exception objects on the heap. **Start here for crashes.**
```bash
scry dumpexceptions
```
```json
{
  "handle": "scry-...", "totalMatches": 1, "truncated": false, "offset": 0, "limit": 1000,
  "exceptions": [
    { "address": "0x...", "type": "System.OutOfMemoryException", "message": "...",
      "hResult": "0x8007000e", "inner": [ { "type": "...", "message": "..." } ] }
  ]
}
```
- `totalMatches: 0` ⇒ no live exception ⇒ likely a hang/leak, not a crash.
- Note: exceptions can linger on the heap after being handled; presence ≠ the fatal one. Corroborate
  with the faulting thread's `currentException` (from `clrthreads`) and `stack`.

## printexception
Full detail + reconstructed stack trace for one exception, by address.
```bash
scry printexception --address 0x1a2b3c
```
```json
{
  "handle": "scry-...", "found": true, "address": "0x...", "type": "...", "message": "...",
  "hResult": "0x...", "inner": [ ... ],
  "stackTrace": [ { "kind": "...", "method": "...", "type": "...", "module": "..." } ]
}
```
`{ "found": false }` (exit 0) ⇒ the address isn't a valid exception object.

## dumpheap
Heap composition (`--stat`, the default) or a paged object listing (`--type`). **Start the `--stat`
form for leaks/OOM.** The first heap command warms an in-memory snapshot (subsequent ones are fast).
```bash
scry dumpheap                       # per-type stats, sorted by total size desc
scry dumpheap --type MyApp.Cache    # paged objects whose type name contains the substring
```
Stats:
```json
{ "handle": "scry-...", "stats": [ { "type": "System.String", "methodTable": "0x...", "count": 34955, "totalSize": 5123456 } ] }
```
Listing:
```json
{ "handle": "scry-...", "totalMatches": 5, "truncated": false, "offset": 0, "limit": 1000,
  "objects": [ { "address": "0x...", "type": "...", "size": 64 } ] }
```
- `--type` is a **case-sensitive substring** match on the full type name (e.g. `Dictionary`,
  `MyApp.`, `[]` for arrays).
- The top entry of `--stat` by `totalSize` is the prime leak suspect → take an address from the
  `--type` listing → `gcroot` it.

## dumpobject
One object's instance fields, by address.
```bash
scry dumpobject --address 0x1a2b3c
```
```json
{ "handle": "scry-...", "found": true, "address": "0x...", "type": "...", "methodTable": "0x...",
  "size": 24, "fields": [ { "name": "_items", "type": "System.Object[]", "offset": 8, "value": "0x..." } ] }
```
- Reference-typed fields render as an address (shallow) — `dumpobject` that address to go deeper.
- Primitive/string fields render as their value. `{ "found": false }` for an invalid address.

## dumparray
An array's elements, paged.
```bash
scry dumparray --address 0x1a2b3c --limit 20
```
```json
{ "handle": "scry-...", "found": true, "address": "0x...", "type": "System.String[]",
  "elementType": "System.String", "length": 100, "truncated": true, "offset": 0, "limit": 20,
  "elements": [ { "index": 0, "value": "..." } ] }
```
Reference elements render as addresses; primitives/strings as values.

## gcroot
Root paths keeping an object alive — **the leak-proving command.**
```bash
scry gcroot --address 0x1a2b3c               # first path (cheap-ish)
scry gcroot --address 0x1a2b3c --max-paths 5 # more retainers
```
```json
{
  "handle": "scry-...", "found": true, "target": "0x...", "rooted": true, "truncated": true,
  "roots": [
    { "rootKind": "StrongHandle", "rootAddress": "0x...", "stackFrame": null,
      "chain": [ { "address": "0x...", "type": "System.Object[]" },
                 { "address": "0x...", "type": "MyApp.Cache" } ] }
  ]
}
```
- `rootKind`: `StrongHandle`, `Stack` (with `stackFrame`), `FinalizerQueue`, `PinnedHandle`, …
- `rooted: false` ⇒ the object is NOT retained (eligible for collection) — often the answer to
  "why is this growing?" is "it isn't; look elsewhere."
- `{ "found": false }` ⇒ invalid address. This walks the whole heap; it's the slowest command and
  defaults to a 120s timeout. Defaults to **one** path; raise `--max-paths` only when you need every
  retainer.

## syncblk
Managed sync blocks acting as live monitors — **the monitor-deadlock command.**
```bash
scry syncblk
```
```json
{
  "handle": "scry-...",
  "blocks": [
    { "index": 2, "objectAddress": "0x...", "objectType": "System.Object", "monitorHeld": true,
      "owner": { "threadAddress": "0x...", "osThreadId": 35460, "managedThreadId": 7 },
      "recursionCount": 1, "waitingThreadCount": 1 }
  ]
}
```
- Only live monitor blocks are listed (non-monitor sync blocks are filtered out as noise).
- `owner` is the lock holder (`null` if unowned); `waitingThreadCount` > 0 means contention.
- Cross-reference `owner.osThreadId` and waiters with `stack --thread` to map the lock-ordering cycle.
- Empty `blocks` ⇒ no contended monitors; a hang is **not** a classic `lock` deadlock — pivot to
  `dumpasync` (async) or `stack` (other blocking primitives: `SemaphoreSlim`, `Task.Wait`, native).

## dumpasync
Async state machines in flight on the heap — **the async-hang command.**
```bash
scry dumpasync
```
```json
{
  "handle": "scry-...", "totalMatches": 2, "truncated": false, "offset": 0, "limit": 1000,
  "machines": [
    { "address": "0x...", "type": "MyApp.Worker+<RunAsync>d__3", "state": 0,
      "status": "suspended at await 0",
      "continuation": { "address": "0x...", "type": "System.Threading.Tasks.Task" } }
  ]
}
```
- `status`: **`suspended at await N`** is the smoking gun for an async hang — that method is parked at
  await point N and never resumed. `running` (`state -1`) / `completed` (`state -2`) are usually not
  the problem.
- `type` is the user's `async` method (`<MethodName>d__N`). `continuation`, when present, is what
  resumes next — follow the chain to find who's ultimately waiting.
- Many `suspended` machines all awaiting the same downstream resource point at that resource.
