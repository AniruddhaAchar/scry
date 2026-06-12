# ADR 0007 — Single binary and distribution

- **Status:** Accepted
- **Date:** 2026-06-12
- **Amends:** [ADR 0004](0004-session-model-analyze-handle.md) (Spawning); reframes the
  "two binaries / separate processes" language in [ADR 0002](0002-grpc-over-uds-and-named-pipes.md)
  and [ADR 0005](0005-configuration-and-logging.md).

## Context

scry has always been described as two binaries: `scry` (short-lived CLI) and `scryd`
(long-lived daemon). That split is a clean *conceptual* boundary, but it makes distribution
awkward:

- The two executables must ship together, be **co-located** (`scry` finds `scryd` via
  `SCRYD_PATH` or next-to-binary), and stay **version-matched** — they share the generated proto,
  so a new `scry` against a stale `scryd` is a latent break.
- A **.NET global tool** — the lowest-friction install for our near-term audience (.NET developers
  analyzing a dump on their own machine: `dotnet tool install -g scry`) — wants a single entry
  assembly. Two separate exes fight that model.

The daemon-vs-CLI split is a *process* and *lifetime* distinction, not necessarily a *binary* one.
A single executable can serve both roles by branching on how it was invoked.

## Decision

**Ship one executable, `scry`, with two run modes selected by the first argument.**

- A hidden internal verb `__host` runs the daemon. It is deliberately *not* registered as a
  System.CommandLine subcommand, so it never appears in `--help`; it exists only as a spawn target.
  ```csharp
  if (args is ["__host", .. var rest])
      return await HostMode.RunAsync(rest);   // Kestrel + gRPC + AnalysisWorker (the old scryd)
  return await CliMode.RunAsync(args);        // the System.CommandLine root (the old scry)
  ```
- **`scry analyze` spawns itself.** Instead of locating a separate `scryd`, it launches
  `Environment.ProcessPath` with `__host --dump <path> --idle-timeout <min> [--verbose]`. The
  detached daemon is, by construction, the identical binary and version.
- **Project shape:** the host code (`HostMode`, `ScryServiceImpl`, `ScryListener`, `HostState`,
  idle shutdown, `HostArgs`) becomes a **library** (`Scry.Host`, `Microsoft.NET.Sdk` +
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — the supported way to host
  Kestrel/gRPC without the Web SDK). The single exe project produces `scry` and references it.
  `Scry.Contracts` / `Scry.Core` / `Scry.Analysis` are unchanged.
- **Removed:** `FindScryd`, the `SCRYD_PATH` environment override, and the separate `scryd` build
  output. `StdHandle.MakeNonInheritable()` stays — we still spawn a detached child and must not leak
  the caller's std handles.

### Distribution

- **Primary channel — .NET global tool.** `<PackAsTool>true</PackAsTool>`,
  `<ToolCommandName>scry</ToolCommandName>`, published to NuGet; `dotnet tool install -g scry`.
  Framework-dependent: requires the **ASP.NET Core 10 runtime** (because the host mode uses Kestrel)
  — satisfied by anyone with the .NET 10 SDK or the ASP.NET Core runtime installed.
- **Secondary — self-contained per-RID archives.** `dotnet publish -r <rid> --self-contained` for
  `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`, zipped and attached to a GitHub
  Release. No runtime install required (~tens of MB each). Per-RID because the host architecture must
  match the dump (x64 dump → x64 host; ADR/CLAUDE core constraint).
- **Release CI.** A tag-triggered (`v*`) GitHub Actions workflow builds the matrix, produces the
  zips + the `.nupkg`, attaches the zips to the Release, and (with a `NUGET_API_KEY` secret) pushes
  the tool to NuGet. One version number; `scry` and its `__host` mode are the same artifact, so they
  can never drift.

## Consequences

- The co-location requirement, version-skew risk, and `SCRYD_PATH`/`FindScryd` machinery all
  disappear — the daemon is the same file as the CLI.
- A global tool becomes trivial; self-contained zips fall out of the same project.
- The "two binaries" framing in earlier ADRs now means "two **modes** of one binary." The
  ADR 0004 **Spawning** section is superseded by the self-exec described here (0004 is amended with a
  pointer). The transport (ADR 0002), session model, registry, and logging are otherwise unchanged —
  the host still registers a `SessionDescriptor`, still binds the dump-derived pipe/socket, still
  logs under the `scryd` app-name in host mode and `scry` in CLI mode.
- The tool's ASP.NET Core runtime dependency is a minor install prerequisite; self-contained zips
  cover environments without it.
- `Scry.Host` as a library keeps the host code modular and unit-testable (e.g. the existing
  `HostArgs` parser tests) without producing a second executable.
