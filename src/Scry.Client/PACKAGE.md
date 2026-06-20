# scry

A [ClrMD](https://github.com/microsoft/clrmd)-based .NET memory-dump analyzer **for AI agents**.
It returns **structured JSON**, not human-readable SOS text, so an agent can chain analysis
steps (`dumpheap` → list a type → `dumpobject` → walk roots) without scraping a debugger console.

## Install

```bash
dotnet tool install -g Scry.Cli
```

Requires the **.NET 10 SDK** (or the ASP.NET Core 10 runtime). Prefer no runtime at all? Grab a
self-contained zip from the [releases page](https://github.com/AniruddhaAchar/scry/releases/latest).

## Use

```bash
scry analyze /path/to/app.dmp     # load a dump (spawns a warm host, holds the runtime open)

scry clrthreads                   # managed threads — start here for hangs/deadlocks
scry syncblk                      # held monitors + owner/waiters (deadlocks)
scry dumpasync                    # async state machines in flight (async hangs)
scry dumpheap                     # heap statistics
scry dumpheap --type System.String
scry dumpobject --address 0xCAFEBABE
scry gcroot --address 0xDECADE    # why is this object still alive?

scry stop                         # release the dump
```

Every command prints JSON to stdout; errors print a JSON `error` object and exit non-zero.

## For AI agents

scry ships with an [agent skill](https://github.com/AniruddhaAchar/scry/tree/main/skills/scry)
that teaches an agent how to drive scry **and** a bounded, give-up-aware reasoning loop — so it
reaches a cited root cause or honestly reports "not enough information" instead of spinning.

Full docs, ADRs, and the roadmap live in the
[GitHub repository](https://github.com/AniruddhaAchar/scry).

## License

MIT
