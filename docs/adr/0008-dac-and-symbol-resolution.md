# ADR 0008 — DAC and symbol resolution

- **Status:** Accepted
- **Date:** 2026-06-12

## Context

ClrMD cannot read a dump's CLR structures without the matching **DAC** (the data-access component,
`mscordaccore.dll`), which must match the dump's runtime **version and architecture** exactly. Early
scry framing assumed "same-machine only" — analyze a dump where its runtime is already installed, so
the DAC is local. That is more conservative than reality.

On **Windows**, ClrMD's `DataTarget` ships a default binary locator that acquires the DAC (and other
needed binaries) from the **Microsoft symbol server** (`msdl.microsoft.com`), honoring
`_NT_SYMBOL_PATH`. For dumps from **released** .NET runtimes, the DAC is published there, so
cross-machine analysis on Windows works **without scry shipping any symbol infrastructure** —
`DataTarget.LoadDump` pulls the DAC on demand into the symbol cache.

Note this is the *DAC*, not source-line PDBs. scry reports method/type/module names, not file:line,
so PDBs are not required for any current command.

## Decision

**Rely on ClrMD's built-in symbol-server DAC resolution; expose a small config surface; do not build
our own symbol infrastructure in v0.0.x.**

- Keep loading via `DataTarget.LoadDump(path)` and let ClrMD's default locator resolve the DAC,
  honoring `_NT_SYMBOL_PATH` and the default Microsoft symbol server on Windows.
- Add an optional `symbols` section to `~/.scry/scry.config.json` (ADR 0005) so the symbol path /
  local cache directory is configurable and, importantly, **cacheable** across sessions:
  ```json
  { "symbols": { "path": "srv*C:\\sym*https://msdl.microsoft.com/download/symbols" } }
  ```
  When set, scry applies it to the `DataTarget` before opening the runtime; when unset, ClrMD's
  defaults apply. `Health`/`analyze` already surface a `FAILED` state with detail if the DAC cannot
  be resolved.
- **Verify** the round trip (a dump whose runtime is not installed locally resolves its DAC from the
  symbol server) and **relax the README caveat** accordingly to: "Windows + released runtimes resolve
  the DAC automatically from the Microsoft symbol server; the first analyze may download it."

## Consequences

- Cross-machine analysis on Windows for released runtimes works today; scry's docs no longer overstate
  the limitation.
- Real remaining gaps, documented rather than solved here:
  - **Offline / air-gapped** hosts (no msdl reachability) — mitigated by a pre-populated symbol cache
    via the config `path`.
  - **Preview / private runtimes** whose DAC is not on msdl — user must supply it on the symbol path.
  - **Linux/macOS** DAC acquisition is less turnkey (often `dotnet-symbol`); broader cross-platform
    symbol robustness is deferred to a later milestone.
- No new heavy dependency: we lean on ClrMD's locator instead of bundling `dotnet-symbol`.
